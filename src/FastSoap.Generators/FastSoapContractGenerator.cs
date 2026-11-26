using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using FastSoap.Generators.Emission;
using FastSoap.Generators.Extraction;
using FastSoap.Generators.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace FastSoap.Generators
{

    /// <summary>
    /// Roslyn incremental source generator for FastSoap <c>[SoapService]</c> contracts,
    /// emitting static operation descriptors, typed endpoint registration helpers, and compile-time diagnostics.
    /// </summary>
    /// <remarks>
    /// Pipeline stages exchange strictly value-equatable immutable models and reconstruct
    /// <see cref="Diagnostic"/> locations only during final reporting.
    /// </remarks>
    [Generator(LanguageNames.CSharp)]
    public sealed class FastSoapContractGenerator : IIncrementalGenerator
    {

        #region Fields

        internal const string CollectConflictsTrackingName = "CollectConflicts";
        internal const string DiscoverContractsTrackingName = "DiscoverContracts";
        internal const string ExtractDiagnosticsTrackingName = "ExtractDiagnostics";
        internal const string ExtractGenerationModelsTrackingName = "ExtractGenerationModels";

        private const string SoapServiceAttributeMetadataName = "FastSoap.Abstractions.SoapServiceAttribute";

        #endregion

        #region Public Methods

        /// <inheritdoc />
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            IncrementalValuesProvider<ServiceExtractionResult> extracted = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    fullyQualifiedMetadataName: SoapServiceAttributeMetadataName,
                    predicate: static (node, _) => node is TypeDeclarationSyntax,
                    transform: ServiceContractExtractor.Extract)
                .Where(static result => result is not null)!
                .Select(static (result, _) => result!)
                .WithTrackingName(DiscoverContractsTrackingName);

            IncrementalValuesProvider<EquatableArray<DiagnosticSpec>> perServiceDiagnostics = extracted
                .Select(static (result, _) => result.Diagnostics)
                .Where(static diags => !diags.IsEmpty)
                .WithTrackingName(ExtractDiagnosticsTrackingName);

            context.RegisterSourceOutput(
                perServiceDiagnostics,
                static (productionContext, diagnostics) =>
                {
                    foreach (DiagnosticSpec spec in diagnostics)
                    {
                        productionContext.ReportDiagnostic(MaterializeDiagnostic(spec));
                    }
                });

            IncrementalValueProvider<EquatableArray<ServiceIdentityModel>> allIdentities = extracted
                .Select(static (result, _) => result.Identity)
                .Collect()
                .Select(static (identities, _) => ToEquatableArray(identities))
                .WithTrackingName("CollectIdentities");

            IncrementalValueProvider<EquatableArray<string>> conflictingRegistrationNames = allIdentities
                .Select(static (identities, _) => ComputeConflictingRegistrationNames(identities))
                .WithTrackingName(CollectConflictsTrackingName);

            context.RegisterSourceOutput(
                allIdentities,
                static (productionContext, identities) =>
                {
                    ReportRegistrationConflicts(productionContext, identities);
                });

            IncrementalValuesProvider<ServiceGenerationModel> generationModels = extracted
                .Select(static (result, _) => result.GenerationModel)
                .Where(static model => model is not null)
                .Select(static (model, _) => model!)
                .WithTrackingName(ExtractGenerationModelsTrackingName);

            IncrementalValuesProvider<(ServiceGenerationModel Model, EquatableArray<string> ConflictingNames)> modelsToEmit =
                generationModels.Combine(conflictingRegistrationNames);

            context.RegisterSourceOutput(
                modelsToEmit,
                static (productionContext, item) =>
                {
                    if (ContainsOrdinal(item.ConflictingNames, item.Model.RegistrationMethodName))
                    {
                        return;
                    }

                    string contractSource = SoapContractEmitter.EmitContractClass(item.Model);
                    string extensionsSource = SoapContractEmitter.EmitRegistrationExtensions(item.Model);

                    productionContext.AddSource(
                        $"{item.Model.HintNamePrefix}.SoapContract.g.cs",
                        SourceText.From(contractSource, Encoding.UTF8));

                    productionContext.AddSource(
                        $"{item.Model.HintNamePrefix}.SoapEndpointRouteBuilderExtensions.g.cs",
                        SourceText.From(extensionsSource, Encoding.UTF8));
                });
        }

        #endregion

        #region Private Methods

        private static EquatableArray<string> ComputeConflictingRegistrationNames(EquatableArray<ServiceIdentityModel> identities)
        {
            if (identities.Count < 2)
            {
                return EquatableArray<string>.Empty;
            }

            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index < identities.Count; index++)
            {
                string name = identities[index].RegistrationMethodName;
                counts.TryGetValue(name, out int count);
                counts[name] = count + 1;
            }

            List<string> conflicts = new List<string>();
            foreach (KeyValuePair<string, int> entry in counts)
            {
                if (entry.Value > 1)
                {
                    conflicts.Add(entry.Key);
                }
            }

            if (conflicts.Count == 0)
            {
                return EquatableArray<string>.Empty;
            }

            conflicts.Sort(StringComparer.Ordinal);
            return new EquatableArray<string>(conflicts.ToArray());
        }

        private static EquatableArray<ServiceIdentityModel> ToEquatableArray(ImmutableArray<ServiceIdentityModel> identities)
        {
            if (identities.IsDefaultOrEmpty)
            {
                return EquatableArray<ServiceIdentityModel>.Empty;
            }

            ServiceIdentityModel[] copy = new ServiceIdentityModel[identities.Length];
            for (int index = 0; index < identities.Length; index++)
            {
                copy[index] = identities[index];
            }

            return new EquatableArray<ServiceIdentityModel>(copy);
        }

        private static bool ContainsOrdinal(EquatableArray<string> items, string target)
        {
            foreach (string item in items)
            {
                if (string.Equals(item, target, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static DiagnosticDescriptor GetDescriptorById(string diagnosticId)
        {
            return diagnosticId switch
            {
                "FS0001" => DiagnosticDescriptors.ServiceContractMissingOrInvalid,
                "FS0002" => DiagnosticDescriptors.DuplicateOrAmbiguousMapping,
                "FS0003" => DiagnosticDescriptors.UnsupportedMethodSignature,
                "FS0004" => DiagnosticDescriptors.MissingExplicitWireMapping,
                "FS0005" => DiagnosticDescriptors.MissingOrIncompatibleXmlContext,
                "FS0006" => DiagnosticDescriptors.InvalidQNameOrWireShape,
                "FS0007" => DiagnosticDescriptors.InaccessibleServiceOrMember,
                "FS0008" => DiagnosticDescriptors.ConflictingRegistrationMethodName,
                _ => throw new InvalidOperationException($"Unknown diagnostic ID '{diagnosticId}'.")
            };
        }

        private static Diagnostic MaterializeDiagnostic(DiagnosticSpec spec)
        {
            DiagnosticDescriptor descriptor = GetDescriptorById(spec.DiagnosticId);
            Location location = spec.Location.ToLocation();
            object?[] messageArgs = new object?[spec.MessageArguments.Count];
            for (int index = 0; index < spec.MessageArguments.Count; index++)
            {
                messageArgs[index] = spec.MessageArguments[index];
            }

            return Diagnostic.Create(descriptor, location, messageArgs);
        }

        private static void ReportRegistrationConflicts(
            SourceProductionContext productionContext,
            EquatableArray<ServiceIdentityModel> identities)
        {
            if (identities.Count < 2)
            {
                return;
            }

            Dictionary<string, List<ServiceIdentityModel>> byMethodName = new Dictionary<string, List<ServiceIdentityModel>>(StringComparer.Ordinal);
            for (int index = 0; index < identities.Count; index++)
            {
                ServiceIdentityModel identity = identities[index];
                if (!byMethodName.TryGetValue(identity.RegistrationMethodName, out List<ServiceIdentityModel>? list))
                {
                    list = new List<ServiceIdentityModel>();
                    byMethodName[identity.RegistrationMethodName] = list;
                }

                list.Add(identity);
            }

            foreach (KeyValuePair<string, List<ServiceIdentityModel>> entry in byMethodName)
            {
                if (entry.Value.Count <= 1)
                {
                    continue;
                }

                for (int index = 0; index < entry.Value.Count; index++)
                {
                    ServiceIdentityModel identity = entry.Value[index];
                    if (identity.HasUserRegistrationMethodConflict)
                    {
                        continue;
                    }

                    productionContext.ReportDiagnostic(
                        Diagnostic.Create(
                            DiagnosticDescriptors.ConflictingRegistrationMethodName,
                            identity.ServiceLocation.ToLocation(),
                            identity.RegistrationMethodName,
                            identity.HintNamePrefix,
                            "another [SoapService] contract producing the same registration method name; set distinct [SoapService(Name = ...)] values"));
                }
            }
        }

        #endregion

    }

}
