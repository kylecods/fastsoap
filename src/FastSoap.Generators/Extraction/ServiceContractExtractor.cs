using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using FastSoap.Generators.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FastSoap.Generators.Extraction
{

    /// <summary>
    /// Extracts immutable, value-equatable contract models and lightweight diagnostic specs
    /// from Roslyn symbols within the incremental transform stage.
    /// </summary>
    internal static class ServiceContractExtractor
    {

        #region Fields

        private const string CancellationTokenMetadataName = "System.Threading.CancellationToken";
        private const string EndpointRouteBuilderMetadataName = "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder";
        private const string SoapOperationAttributeMetadataName = "FastSoap.Abstractions.SoapOperationAttribute";
        private const string SoapServiceAttributeMetadataName = "FastSoap.Abstractions.SoapServiceAttribute";
        private const string SoapWireContractAttributeMetadataName = "FastSoap.Abstractions.SoapWireContractAttribute";
        private const string StreamMetadataName = "System.IO.Stream";
        private const string TaskGenericMetadataName = "System.Threading.Tasks.Task`1";
        private const string ValueTaskGenericMetadataName = "System.Threading.Tasks.ValueTask`1";
        private const string XmlElementAttributeMetadataName = "CloudNimble.Text.Xml.Serialization.XmlElementAttribute";
        private const string XmlElementGenericAttributeMetadataName = "CloudNimble.Text.Xml.Serialization.XmlElementAttribute`1";
        private const string XmlRootAttributeMetadataName = "CloudNimble.Text.Xml.Serialization.XmlRootAttribute";
        private const string XmlSerializableGenericAttributeMetadataName = "CloudNimble.Text.Xml.Serialization.XmlSerializableAttribute`1";
        private const string XmlSerializerContextMetadataName = "CloudNimble.Text.Xml.Serialization.XmlSerializerContext";

        #endregion

        #region Internal Methods

        /// <summary>
        /// Extracts contract metadata and diagnostics for one symbol annotated with <c>[SoapService]</c>.
        /// </summary>
        /// <param name="context">The Roslyn attribute syntax context.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>An immutable, value-equatable <see cref="ServiceExtractionResult"/>.</returns>
        internal static ServiceExtractionResult? Extract(
            GeneratorAttributeSyntaxContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (context.TargetSymbol is not INamedTypeSymbol serviceSymbol)
            {
                return null;
            }

            KnownSymbols known = KnownSymbols.Create(context.SemanticModel.Compilation);
            List<DiagnosticSpec> diagnostics = new List<DiagnosticSpec>();

            AttributeData? serviceAttribute = GetAttribute(serviceSymbol, known.SoapServiceAttribute);
            if (serviceAttribute is null)
            {
                return null;
            }

            EquatableLocation typeLocation = EquatableLocation.Create(GetPrimaryLocation(serviceSymbol, context.TargetNode));
            EquatableLocation serviceAttrLocation = EquatableLocation.Create(GetAttributeLocation(serviceAttribute, typeLocation));

            string serviceDisplayName = serviceSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            string rawNamespace = string.Empty;
            string? explicitServiceName = null;
            bool hasExplicitName = false;

            foreach (KeyValuePair<string, TypedConstant> namedArg in serviceAttribute.NamedArguments)
            {
                if (string.Equals(namedArg.Key, "Namespace", StringComparison.Ordinal))
                {
                    rawNamespace = namedArg.Value.Value as string ?? string.Empty;
                }
                else if (string.Equals(namedArg.Key, "Name", StringComparison.Ordinal))
                {
                    hasExplicitName = true;
                    explicitServiceName = namedArg.Value.Value as string;
                }
            }

            string serviceName = hasExplicitName
                ? (explicitServiceName ?? string.Empty)
                : DeriveDefaultServiceName(serviceSymbol);

            string rawContractName = hasExplicitName && !string.IsNullOrWhiteSpace(explicitServiceName)
                ? explicitServiceName!
                : serviceSymbol.Name;

            bool serviceInvalid = false;

            if (serviceSymbol.IsGenericType || serviceSymbol.TypeParameters.Length > 0)
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0003",
                    typeLocation,
                    serviceDisplayName,
                    "open generic SOAP service contracts are not supported"));
                serviceInvalid = true;
            }

            if (serviceSymbol.TypeKind != TypeKind.Interface && serviceSymbol.TypeKind != TypeKind.Class)
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0001",
                    serviceAttrLocation,
                    serviceDisplayName,
                    "must be declared on an interface or non-static class"));
                serviceInvalid = true;
            }
            else if (serviceSymbol.IsStatic)
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0001",
                    typeLocation,
                    serviceDisplayName,
                    "cannot be declared on a static class"));
                serviceInvalid = true;
            }

            if (!IsTypeAccessibleFromGeneratedCode(serviceSymbol, known.Compilation))
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0007",
                    typeLocation,
                    serviceDisplayName,
                    "must be public or internal (not private, protected, or file-local)"));
                serviceInvalid = true;
            }

            if (!XmlNameValidator.IsValidRequiredServiceNamespace(rawNamespace))
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0001",
                    serviceAttrLocation,
                    serviceDisplayName,
                    "must declare a non-empty XML Namespace without whitespace or control characters"));
                serviceInvalid = true;
            }

            if (!XmlNameValidator.IsValidServiceOrOperationIdentifier(serviceName))
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0001",
                    serviceAttrLocation,
                    serviceDisplayName,
                    $"has an invalid service Name '{serviceName}'"));
                serviceInvalid = true;
            }

            string targetNamespace = serviceSymbol.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : serviceSymbol.ContainingNamespace.ToDisplayString();

            string registrationMethodName = $"Map{serviceName}";
            string contractClassName = $"{serviceName}SoapContract";
            string hintNamePrefix = BuildHintNamePrefix(serviceSymbol);
            bool isServicePublic = IsEffectivelyPublic(serviceSymbol);

            bool hasUserMethodConflict = HasExistingRegistrationMethod(
                known.Compilation,
                known.EndpointRouteBuilder,
                registrationMethodName);

            bool hasUserContractTypeConflict = HasExistingType(
                serviceSymbol.ContainingNamespace,
                contractClassName);

            if (hasUserMethodConflict)
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0008",
                    serviceAttrLocation,
                    registrationMethodName,
                    serviceDisplayName,
                    "an existing method with the same signature in Microsoft.AspNetCore.Builder"));
                serviceInvalid = true;
            }

            if (hasUserContractTypeConflict)
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0008",
                    serviceAttrLocation,
                    contractClassName,
                    serviceDisplayName,
                    $"an existing type '{contractClassName}' in the target namespace"));
                serviceInvalid = true;
            }

            AttributeData? serviceWireAttribute = GetAttribute(serviceSymbol, known.SoapWireContractAttribute);
            INamedTypeSymbol? serviceContextType = null;
            EquatableLocation serviceWireLocation = serviceWireAttribute is null
                ? serviceAttrLocation
                : EquatableLocation.Create(GetAttributeLocation(serviceWireAttribute, serviceAttrLocation));

            if (serviceWireAttribute is not null)
            {
                serviceContextType = ExtractContextType(serviceWireAttribute);
            }

            List<IMethodSymbol> operationMethods = CollectOperationMethods(serviceSymbol, known.SoapOperationAttribute);
            Dictionary<string, int> methodNameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (IMethodSymbol method in operationMethods)
            {
                methodNameCounts.TryGetValue(method.Name, out int count);
                methodNameCounts[method.Name] = count + 1;
            }

            List<OperationGenerationModel> operations = new List<OperationGenerationModel>(operationMethods.Count);
            HashSet<string> seenActions = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> seenOperationNames = new HashSet<string>(StringComparer.Ordinal);
            HashSet<(string NamespaceUri, string LocalName)> seenRequestQNames = new HashSet<(string NamespaceUri, string LocalName)>();

            bool anyOperationFailed = false;

            foreach (IMethodSymbol method in operationMethods)
            {
                cancellationToken.ThrowIfCancellationRequested();

                OperationExtractionOutcome outcome = ExtractOperation(
                    method,
                    serviceSymbol,
                    serviceDisplayName,
                    rawNamespace,
                    rawContractName,
                    serviceContextType,
                    serviceWireLocation,
                    methodNameCounts[method.Name] > 1,
                    known,
                    diagnostics);

                if (outcome.Model is null)
                {
                    anyOperationFailed = true;
                    continue;
                }

                OperationGenerationModel opModel = outcome.Model;
                bool duplicate = false;

                if (!seenOperationNames.Add(opModel.OperationName))
                {
                    diagnostics.Add(CreateDiagnostic(
                        "FS0002",
                        outcome.OperationLocation,
                        opModel.OperationName,
                        serviceDisplayName,
                        $"duplicate operation name '{opModel.OperationName}'"));
                    duplicate = true;
                }

                if (!seenActions.Add(opModel.Action))
                {
                    diagnostics.Add(CreateDiagnostic(
                        "FS0002",
                        outcome.OperationLocation,
                        opModel.OperationName,
                        serviceDisplayName,
                        $"duplicate SOAP action '{opModel.Action}'"));
                    duplicate = true;
                }

                if (!seenRequestQNames.Add((opModel.RequestNamespace, opModel.RequestLocalName)))
                {
                    diagnostics.Add(CreateDiagnostic(
                        "FS0002",
                        outcome.OperationLocation,
                        opModel.OperationName,
                        serviceDisplayName,
                        $"ambiguous request Body element '{{{opModel.RequestNamespace}}}{opModel.RequestLocalName}'"));
                    duplicate = true;
                }

                if (duplicate)
                {
                    anyOperationFailed = true;
                    continue;
                }

                operations.Add(opModel);
            }

            operations.Sort(static (left, right) => string.CompareOrdinal(left.OperationName, right.OperationName));

            ServiceGenerationModel? generationModel = null;
            if (!serviceInvalid && !anyOperationFailed && diagnostics.Count == 0)
            {
                generationModel = new ServiceGenerationModel(
                    HintNamePrefix: hintNamePrefix,
                    IsPublic: isServicePublic,
                    Operations: new EquatableArray<OperationGenerationModel>(operations.ToArray()),
                    RegistrationMethodName: registrationMethodName,
                    ServiceFullyQualifiedName: serviceSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ServiceName: serviceName,
                    ServiceNamespace: rawNamespace,
                    TargetNamespace: targetNamespace);
            }

            ServiceIdentityModel identity = new ServiceIdentityModel(
                HasUserContractTypeConflict: hasUserContractTypeConflict,
                HasUserRegistrationMethodConflict: hasUserMethodConflict,
                HintNamePrefix: hintNamePrefix,
                RegistrationMethodName: registrationMethodName,
                ServiceLocation: serviceAttrLocation,
                ServiceName: serviceName,
                TargetNamespace: targetNamespace);

            return new ServiceExtractionResult(
                Diagnostics: new EquatableArray<DiagnosticSpec>(diagnostics.ToArray()),
                GenerationModel: generationModel,
                Identity: identity);
        }

        #endregion

        #region Private Methods

        private static string BuildHintNamePrefix(INamedTypeSymbol serviceSymbol)
        {
            string fullName = serviceSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (fullName.StartsWith("global::", StringComparison.Ordinal))
            {
                fullName = fullName.Substring("global::".Length);
            }

            char[] chars = new char[fullName.Length];
            for (int index = 0; index < fullName.Length; index++)
            {
                char current = fullName[index];
                chars[index] = current switch
                {
                    '<' or '>' or ',' or ' ' or ':' or '+' => '_',
                    _ => current
                };
            }

            return new string(chars);
        }

        private static List<IMethodSymbol> CollectOperationMethods(
            INamedTypeSymbol serviceSymbol,
            INamedTypeSymbol? soapOperationAttribute)
        {
            List<IMethodSymbol> results = new List<IMethodSymbol>();
            if (soapOperationAttribute is null)
            {
                return results;
            }

            HashSet<IMethodSymbol> seen = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

            AddOperationsFromType(serviceSymbol, soapOperationAttribute, seen, results);

            if (serviceSymbol.TypeKind == TypeKind.Interface)
            {
                foreach (INamedTypeSymbol baseInterface in serviceSymbol.AllInterfaces)
                {
                    AddOperationsFromType(baseInterface, soapOperationAttribute, seen, results);
                }
            }

            return results;
        }

        private static void AddOperationsFromType(
            INamedTypeSymbol typeSymbol,
            INamedTypeSymbol soapOperationAttribute,
            HashSet<IMethodSymbol> seen,
            List<IMethodSymbol> results)
        {
            foreach (ISymbol member in typeSymbol.GetMembers())
            {
                if (member is not IMethodSymbol method || method.MethodKind != MethodKind.Ordinary)
                {
                    continue;
                }

                if (GetAttribute(method, soapOperationAttribute) is null)
                {
                    continue;
                }

                if (seen.Add(method))
                {
                    results.Add(method);
                }
            }
        }

        private static DiagnosticSpec CreateDiagnostic(
            string id,
            EquatableLocation location,
            params string[] args)
        {
            return new DiagnosticSpec(id, location, new EquatableArray<string>(args));
        }

        private static string DeriveDefaultOperationName(string methodName)
        {
            if (methodName.EndsWith("Async", StringComparison.Ordinal) && methodName.Length > 5)
            {
                return methodName.Substring(0, methodName.Length - 5);
            }

            return methodName;
        }

        private static string DeriveDefaultServiceName(INamedTypeSymbol serviceSymbol)
        {
            string name = serviceSymbol.Name;
            if (serviceSymbol.TypeKind == TypeKind.Interface
                && name.Length >= 2
                && name[0] == 'I'
                && char.IsUpper(name[1]))
            {
                return name.Substring(1);
            }

            return name;
        }

        private static INamedTypeSymbol? ExtractContextType(AttributeData wireAttribute)
        {
            if (wireAttribute.ConstructorArguments.Length == 1
                && wireAttribute.ConstructorArguments[0].Value is INamedTypeSymbol ctorContext)
            {
                return ctorContext;
            }

            foreach (KeyValuePair<string, TypedConstant> namedArg in wireAttribute.NamedArguments)
            {
                if (string.Equals(namedArg.Key, "ContextType", StringComparison.Ordinal)
                    && namedArg.Value.Value is INamedTypeSymbol namedContext)
                {
                    return namedContext;
                }
            }

            return null;
        }

        private static OperationExtractionOutcome ExtractOperation(
            IMethodSymbol method,
            INamedTypeSymbol serviceSymbol,
            string serviceDisplayName,
            string serviceNamespace,
            string rawContractName,
            INamedTypeSymbol? serviceContextType,
            EquatableLocation serviceWireLocation,
            bool isOverloaded,
            KnownSymbols known,
            List<DiagnosticSpec> diagnostics)
        {
            AttributeData opAttribute = GetAttribute(method, known.SoapOperationAttribute)!;
            EquatableLocation methodLocation = EquatableLocation.Create(GetPrimaryLocation(method, null));
            EquatableLocation opAttrLocation = EquatableLocation.Create(GetAttributeLocation(opAttribute, methodLocation));

            bool failed = false;

            if (serviceSymbol.TypeKind == TypeKind.Class && !IsMemberAccessibleFromGeneratedCode(method, known.Compilation))
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0007",
                    methodLocation,
                    method.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    "operation method is not accessible from generated code"));
                failed = true;
            }

            if (isOverloaded)
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0003",
                    methodLocation,
                    method.Name,
                    $"overloaded SOAP operation methods named '{method.Name}' are not supported"));
                failed = true;
            }

            if (method.IsStatic)
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0003",
                    methodLocation,
                    method.Name,
                    "SOAP operations cannot be static"));
                failed = true;
            }

            if (method.IsGenericMethod || method.TypeParameters.Length > 0)
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0003",
                    methodLocation,
                    method.Name,
                    "open generic SOAP operation methods are not supported"));
                failed = true;
            }

            if (!TryValidateMethodParameters(method, methodLocation, known, diagnostics, out ITypeSymbol? inputParamType, out bool hasCancellationToken))
            {
                failed = true;
            }

            if (!TryValidateReturnType(method, methodLocation, known, diagnostics, out ITypeSymbol? returnInnerType))
            {
                failed = true;
            }

            string? explicitOpName = null;
            bool hasExplicitOpName = false;
            string? explicitAction = null;
            bool hasExplicitAction = false;
            string? explicitReplyAction = null;
            bool hasExplicitReplyAction = false;
            string? explicitRequestName = null;
            bool hasExplicitRequestName = false;
            string? explicitResponseName = null;
            bool hasExplicitResponseName = false;

            foreach (KeyValuePair<string, TypedConstant> namedArg in opAttribute.NamedArguments)
            {
                switch (namedArg.Key)
                {
                    case "Name":
                        hasExplicitOpName = true;
                        explicitOpName = namedArg.Value.Value as string;
                        break;
                    case "Action":
                        hasExplicitAction = true;
                        explicitAction = namedArg.Value.Value as string;
                        break;
                    case "ReplyAction":
                        hasExplicitReplyAction = true;
                        explicitReplyAction = namedArg.Value.Value as string;
                        break;
                    case "RequestName":
                        hasExplicitRequestName = true;
                        explicitRequestName = namedArg.Value.Value as string;
                        break;
                    case "ResponseName":
                        hasExplicitResponseName = true;
                        explicitResponseName = namedArg.Value.Value as string;
                        break;
                }
            }

            string operationName = hasExplicitOpName
                ? (explicitOpName ?? string.Empty)
                : DeriveDefaultOperationName(method.Name);

            if (!XmlNameValidator.IsValidServiceOrOperationIdentifier(operationName))
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0006",
                    opAttrLocation,
                    method.Name,
                    $"operation name '{operationName}' must be a valid XML NCName and C# identifier"));
                failed = true;
            }

            string action;
            if (hasExplicitAction)
            {
                if (!XmlNameValidator.IsValidAction(explicitAction))
                {
                    diagnostics.Add(CreateDiagnostic(
                        "FS0002",
                        opAttrLocation,
                        operationName,
                        serviceDisplayName,
                        "Action cannot be null, empty, whitespace, or contain control characters"));
                    failed = true;
                    action = string.Empty;
                }
                else
                {
                    action = explicitAction!;
                }
            }
            else
            {
                string prefix = serviceNamespace.TrimEnd('/');
                action = $"{prefix}/{rawContractName}/{operationName}";
            }

            string? replyAction = null;
            if (hasExplicitReplyAction && explicitReplyAction is not null)
            {
                if (!XmlNameValidator.IsValidAction(explicitReplyAction))
                {
                    diagnostics.Add(CreateDiagnostic(
                        "FS0002",
                        opAttrLocation,
                        operationName,
                        serviceDisplayName,
                        "ReplyAction cannot be empty, whitespace, or contain control characters"));
                    failed = true;
                }
                else
                {
                    replyAction = explicitReplyAction;
                }
            }

            if (hasExplicitRequestName && !XmlNameValidator.IsValidLocalName(explicitRequestName))
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0006",
                    opAttrLocation,
                    method.Name,
                    $"RequestName '{explicitRequestName}' is not a valid XML NCName"));
                failed = true;
            }

            if (hasExplicitResponseName && !XmlNameValidator.IsValidLocalName(explicitResponseName))
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0006",
                    opAttrLocation,
                    method.Name,
                    $"ResponseName '{explicitResponseName}' is not a valid XML NCName"));
                failed = true;
            }

            AttributeData? opWireAttribute = GetAttribute(method, known.SoapWireContractAttribute);
            if (opWireAttribute is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0004",
                    methodLocation,
                    operationName,
                    "missing [SoapWireContract] attribute declaring RequestType and ResponseType"));
                return new OperationExtractionOutcome(null, opAttrLocation);
            }

            EquatableLocation opWireLocation = EquatableLocation.Create(GetAttributeLocation(opWireAttribute, methodLocation));

            INamedTypeSymbol? opContextType = ExtractContextType(opWireAttribute);
            INamedTypeSymbol? resolvedContextType = opContextType ?? serviceContextType;
            EquatableLocation contextLocation = opContextType is not null ? opWireLocation : serviceWireLocation;

            ITypeSymbol? requestWireSymbol = null;
            ITypeSymbol? responseWireSymbol = null;
            string? requestMemberName = null;
            bool hasRequestMemberName = false;
            string? responseMemberName = null;
            bool hasResponseMemberName = false;
            string? requestTypeInfoPropOverride = null;
            bool hasRequestTypeInfoPropOverride = false;
            string? responseTypeInfoPropOverride = null;
            bool hasResponseTypeInfoPropOverride = false;

            foreach (KeyValuePair<string, TypedConstant> namedArg in opWireAttribute.NamedArguments)
            {
                switch (namedArg.Key)
                {
                    case "RequestType":
                        requestWireSymbol = namedArg.Value.Value as ITypeSymbol;
                        break;
                    case "ResponseType":
                        responseWireSymbol = namedArg.Value.Value as ITypeSymbol;
                        break;
                    case "RequestMemberName":
                        hasRequestMemberName = true;
                        requestMemberName = namedArg.Value.Value as string;
                        break;
                    case "ResponseMemberName":
                        hasResponseMemberName = true;
                        responseMemberName = namedArg.Value.Value as string;
                        break;
                    case "RequestTypeInfoPropertyName":
                        hasRequestTypeInfoPropOverride = true;
                        requestTypeInfoPropOverride = namedArg.Value.Value as string;
                        break;
                    case "ResponseTypeInfoPropertyName":
                        hasResponseTypeInfoPropOverride = true;
                        responseTypeInfoPropOverride = namedArg.Value.Value as string;
                        break;
                }
            }

            if (requestWireSymbol is null || requestWireSymbol.TypeKind == TypeKind.Error)
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0004",
                    opWireLocation,
                    operationName,
                    "RequestType must be explicitly specified"));
                failed = true;
            }

            if (responseWireSymbol is null || responseWireSymbol.TypeKind == TypeKind.Error)
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0004",
                    opWireLocation,
                    operationName,
                    "ResponseType must be explicitly specified"));
                failed = true;
            }

            if (resolvedContextType is null || resolvedContextType.TypeKind == TypeKind.Error)
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0005",
                    contextLocation,
                    operationName,
                    "no XmlSerializerContext type was declared via [SoapWireContract(typeof(...))] on the service or operation"));
                failed = true;
            }
            else
            {
                if (!IsTypeAccessibleFromGeneratedCode(resolvedContextType, known.Compilation))
                {
                    diagnostics.Add(CreateDiagnostic(
                        "FS0007",
                        contextLocation,
                        resolvedContextType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                        "XML serializer context type is inaccessible from generated code"));
                    failed = true;
                }

                if (!InheritsFrom(resolvedContextType, known.XmlSerializerContext)
                    || resolvedContextType.IsAbstract
                    || resolvedContextType.IsGenericType)
                {
                    diagnostics.Add(CreateDiagnostic(
                        "FS0005",
                        contextLocation,
                        operationName,
                        $"context type '{resolvedContextType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}' must be a concrete non-generic subclass of XmlSerializerContext"));
                    failed = true;
                }
            }

            string reqContextProp = string.Empty;
            string respContextProp = string.Empty;
            string reqQNameNs = string.Empty;
            string reqQNameLocal = string.Empty;
            string respQNameNs = string.Empty;
            string respQNameLocal = string.Empty;

            if (requestWireSymbol is not null && requestWireSymbol.TypeKind != TypeKind.Error)
            {
                if (!ValidateWireDtoShapeAndQName(
                    requestWireSymbol,
                    explicitRequestName,
                    isRequest: true,
                    requireParameterlessCtor: true,
                    opWireLocation,
                    known,
                    diagnostics,
                    out reqQNameNs,
                    out reqQNameLocal))
                {
                    failed = true;
                }

                if (inputParamType is not null && !ValidateRequestMemberMapping(
                    requestWireSymbol,
                    hasRequestMemberName,
                    requestMemberName,
                    inputParamType,
                    operationName,
                    opWireLocation,
                    known,
                    diagnostics))
                {
                    failed = true;
                }

                if (resolvedContextType is not null
                    && resolvedContextType.TypeKind != TypeKind.Error
                    && InheritsFrom(resolvedContextType, known.XmlSerializerContext))
                {
                    if (!ValidateContextRegistration(
                        resolvedContextType,
                        requestWireSymbol,
                        hasRequestTypeInfoPropOverride,
                        requestTypeInfoPropOverride,
                        operationName,
                        contextLocation,
                        known,
                        diagnostics,
                        out reqContextProp))
                    {
                        failed = true;
                    }
                }
            }

            if (responseWireSymbol is not null && responseWireSymbol.TypeKind != TypeKind.Error)
            {
                bool needsCtorForWrapper = hasResponseMemberName && responseMemberName is not null;
                if (!ValidateWireDtoShapeAndQName(
                    responseWireSymbol,
                    explicitResponseName,
                    isRequest: false,
                    requireParameterlessCtor: needsCtorForWrapper,
                    opWireLocation,
                    known,
                    diagnostics,
                    out respQNameNs,
                    out respQNameLocal))
                {
                    failed = true;
                }

                if (returnInnerType is not null && !ValidateResponseMemberMapping(
                    responseWireSymbol,
                    hasResponseMemberName,
                    responseMemberName,
                    returnInnerType,
                    operationName,
                    opWireLocation,
                    known,
                    diagnostics))
                {
                    failed = true;
                }

                if (resolvedContextType is not null
                    && resolvedContextType.TypeKind != TypeKind.Error
                    && InheritsFrom(resolvedContextType, known.XmlSerializerContext))
                {
                    if (!ValidateContextRegistration(
                        resolvedContextType,
                        responseWireSymbol,
                        hasResponseTypeInfoPropOverride,
                        responseTypeInfoPropOverride,
                        operationName,
                        contextLocation,
                        known,
                        diagnostics,
                        out respContextProp))
                    {
                        failed = true;
                    }
                }
            }

            if (failed || returnInnerType is null || requestWireSymbol is null || responseWireSymbol is null || resolvedContextType is null)
            {
                return new OperationExtractionOutcome(null, opAttrLocation);
            }

            OperationGenerationModel model = new OperationGenerationModel(
                Action: action,
                HasCancellationToken: hasCancellationToken,
                MethodName: method.Name,
                OperationName: operationName,
                ReplyAction: replyAction,
                RequestContextFullyQualifiedName: resolvedContextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                RequestContextPropertyName: reqContextProp,
                RequestLocalName: reqQNameLocal,
                RequestMemberName: requestMemberName,
                RequestNamespace: reqQNameNs,
                RequestWireFullyQualifiedName: requestWireSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                ResponseContextFullyQualifiedName: resolvedContextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                ResponseContextPropertyName: respContextProp,
                ResponseLocalName: respQNameLocal,
                ResponseMemberName: responseMemberName,
                ResponseNamespace: respQNameNs,
                ResponseWireFullyQualifiedName: responseWireSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                ServiceReturnInnerFullyQualifiedName: returnInnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

            return new OperationExtractionOutcome(model, opAttrLocation);
        }

        private static IPropertySymbol? FindProperty(ITypeSymbol type, string propertyName)
        {
            ITypeSymbol? current = type;
            while (current is not null)
            {
                foreach (ISymbol member in current.GetMembers(propertyName))
                {
                    if (member is IPropertySymbol property)
                    {
                        return property;
                    }
                }

                current = current.BaseType;
            }

            return null;
        }

        private static AttributeData? GetAttribute(ISymbol symbol, INamedTypeSymbol? attributeType)
        {
            if (attributeType is null)
            {
                return null;
            }

            foreach (AttributeData attribute in symbol.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType))
                {
                    return attribute;
                }
            }

            return null;
        }

        private static Location GetAttributeLocation(AttributeData attribute, EquatableLocation fallback)
        {
            if (attribute.ApplicationSyntaxReference is not null)
            {
                return attribute.ApplicationSyntaxReference.GetSyntax().GetLocation();
            }

            return fallback.ToLocation();
        }

        private static Location GetPrimaryLocation(ISymbol symbol, SyntaxNode? fallbackNode)
        {
            ImmutableArray<Location> locations = symbol.Locations;
            if (!locations.IsDefaultOrEmpty)
            {
                for (int index = 0; index < locations.Length; index++)
                {
                    if (locations[index].IsInSource)
                    {
                        return locations[index];
                    }
                }
            }

            return fallbackNode is not null ? fallbackNode.GetLocation() : Location.None;
        }

        private static bool HasAccessibleParameterlessConstructor(
            INamedTypeSymbol typeSymbol,
            Compilation compilation,
            out bool foundInaccessibleCtor)
        {
            foundInaccessibleCtor = false;

            if (typeSymbol.TypeKind == TypeKind.Struct)
            {
                return true;
            }

            foreach (IMethodSymbol ctor in typeSymbol.InstanceConstructors)
            {
                if (ctor.Parameters.Length == 0)
                {
                    if (compilation.IsSymbolAccessibleWithin(ctor, compilation.Assembly))
                    {
                        return true;
                    }

                    foundInaccessibleCtor = true;
                }
            }

            return false;
        }

        private static bool HasExistingRegistrationMethod(
            Compilation compilation,
            INamedTypeSymbol? endpointRouteBuilderType,
            string registrationMethodName)
        {
            INamespaceSymbol? targetNs = FindNamespace(
                compilation.GlobalNamespace,
                new[] { "Microsoft", "AspNetCore", "Builder" });

            if (targetNs is null)
            {
                return false;
            }

            foreach (INamedTypeSymbol type in targetNs.GetTypeMembers())
            {
                if (!type.IsStatic)
                {
                    continue;
                }

                foreach (ISymbol member in type.GetMembers(registrationMethodName))
                {
                    if (member is IMethodSymbol method
                        && method.IsExtensionMethod
                        && method.Parameters.Length == 2
                        && method.Parameters[1].Type.SpecialType == SpecialType.System_String)
                    {
                        if (endpointRouteBuilderType is null
                            || SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, endpointRouteBuilderType))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool HasExistingType(INamespaceSymbol? targetNamespace, string typeName)
        {
            if (targetNamespace is null)
            {
                return false;
            }

            foreach (INamedTypeSymbol existing in targetNamespace.GetTypeMembers(typeName))
            {
                if (existing.Arity == 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static INamespaceSymbol? FindNamespace(INamespaceSymbol root, string[] segments)
        {
            INamespaceSymbol? current = root;
            for (int index = 0; index < segments.Length; index++)
            {
                INamespaceSymbol? next = null;
                foreach (INamespaceSymbol child in current.GetNamespaceMembers())
                {
                    if (string.Equals(child.Name, segments[index], StringComparison.Ordinal))
                    {
                        next = child;
                        break;
                    }
                }

                if (next is null)
                {
                    return null;
                }

                current = next;
            }

            return current;
        }

        private static bool InheritsFrom(INamedTypeSymbol typeSymbol, INamedTypeSymbol? baseType)
        {
            if (baseType is null)
            {
                return false;
            }

            INamedTypeSymbol? current = typeSymbol.BaseType;
            while (current is not null)
            {
                if (SymbolEqualityComparer.Default.Equals(current, baseType))
                {
                    return true;
                }

                current = current.BaseType;
            }

            return false;
        }

        private static bool IsEffectivelyPublic(INamedTypeSymbol typeSymbol)
        {
            INamedTypeSymbol? current = typeSymbol;
            while (current is not null)
            {
                if (current.DeclaredAccessibility != Accessibility.Public)
                {
                    return false;
                }

                current = current.ContainingType;
            }

            return true;
        }

        private static bool IsImplicitlyConvertible(ITypeSymbol source, ITypeSymbol target, Compilation compilation)
        {
            if (SymbolEqualityComparer.Default.Equals(source, target))
            {
                return true;
            }

            Conversion conversion = compilation.ClassifyConversion(source, target);
            return conversion.IsIdentity || conversion.IsImplicit;
        }

        private static bool IsMemberAccessibleFromGeneratedCode(ISymbol symbol, Compilation compilation)
        {
            if (symbol.DeclaredAccessibility is Accessibility.Private or Accessibility.Protected or Accessibility.ProtectedAndInternal)
            {
                return false;
            }

            return compilation.IsSymbolAccessibleWithin(symbol, compilation.Assembly);
        }

        private static bool IsPartialType(INamedTypeSymbol typeSymbol)
        {
            foreach (SyntaxReference syntaxRef in typeSymbol.DeclaringSyntaxReferences)
            {
                if (syntaxRef.GetSyntax() is TypeDeclarationSyntax typeDecl)
                {
                    foreach (SyntaxToken modifier in typeDecl.Modifiers)
                    {
                        if (modifier.IsKind(SyntaxKind.PartialKeyword))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool IsTypeAccessibleFromGeneratedCode(ITypeSymbol typeSymbol, Compilation compilation)
        {
            if (typeSymbol is INamedTypeSymbol named)
            {
                INamedTypeSymbol? current = named;
                while (current is not null)
                {
                    if (current.IsFileLocal)
                    {
                        return false;
                    }

                    if (current.DeclaredAccessibility is Accessibility.Private or Accessibility.Protected or Accessibility.ProtectedAndInternal)
                    {
                        return false;
                    }

                    if (!compilation.IsSymbolAccessibleWithin(current, compilation.Assembly))
                    {
                        return false;
                    }

                    current = current.ContainingType;
                }

                if (named.IsGenericType)
                {
                    foreach (ITypeSymbol typeArg in named.TypeArguments)
                    {
                        if (!IsTypeAccessibleFromGeneratedCode(typeArg, compilation))
                        {
                            return false;
                        }
                    }
                }

                return true;
            }

            return compilation.IsSymbolAccessibleWithin(typeSymbol, compilation.Assembly);
        }

        private static bool IsUnsupportedTypeShape(ITypeSymbol typeSymbol, KnownSymbols known)
        {
            if (typeSymbol.IsRefLikeType
                || typeSymbol.TypeKind == TypeKind.Pointer
                || typeSymbol.TypeKind == TypeKind.FunctionPointer
                || typeSymbol.TypeKind == TypeKind.Interface
                || typeSymbol.TypeKind == TypeKind.Delegate
                || typeSymbol.TypeKind == TypeKind.Enum
                || typeSymbol.TypeKind == TypeKind.Array
                || typeSymbol.TypeKind == TypeKind.Dynamic
                || typeSymbol.IsStatic
                || typeSymbol.IsAbstract)
            {
                return true;
            }

            if (typeSymbol.SpecialType == SpecialType.System_Object
                || typeSymbol.SpecialType == SpecialType.System_String
                || typeSymbol.SpecialType == SpecialType.System_Void)
            {
                return true;
            }

            if (typeSymbol.SpecialType >= SpecialType.System_Boolean && typeSymbol.SpecialType <= SpecialType.System_DateTime)
            {
                return true;
            }

            if (known.StreamType is not null && SymbolEqualityComparer.Default.Equals(typeSymbol, known.StreamType))
            {
                return true;
            }

            string ns = typeSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            if (ns.Equals("System.Xml", StringComparison.Ordinal)
                || ns.StartsWith("System.Xml.", StringComparison.Ordinal))
            {
                return true;
            }

            return false;
        }

        private static bool TryValidateMethodParameters(
            IMethodSymbol method,
            EquatableLocation methodLocation,
            KnownSymbols known,
            List<DiagnosticSpec> diagnostics,
            out ITypeSymbol? inputParamType,
            out bool hasCancellationToken)
        {
            inputParamType = null;
            hasCancellationToken = false;

            ImmutableArray<IParameterSymbol> parameters = method.Parameters;
            bool valid = true;

            foreach (IParameterSymbol parameter in parameters)
            {
                EquatableLocation paramLocation = EquatableLocation.Create(GetPrimaryLocation(parameter, null));

                if (parameter.RefKind != RefKind.None)
                {
                    diagnostics.Add(CreateDiagnostic(
                        "FS0003",
                        paramLocation,
                        method.Name,
                        $"parameter '{parameter.Name}' uses unsupported '{parameter.RefKind}' modifier"));
                    valid = false;
                }

                if (parameter.IsParams)
                {
                    diagnostics.Add(CreateDiagnostic(
                        "FS0003",
                        paramLocation,
                        method.Name,
                        $"params parameter '{parameter.Name}' is not supported"));
                    valid = false;
                }

                if (parameter.Type.IsRefLikeType
                    || parameter.Type.TypeKind == TypeKind.Pointer
                    || parameter.Type.TypeKind == TypeKind.FunctionPointer)
                {
                    diagnostics.Add(CreateDiagnostic(
                        "FS0003",
                        paramLocation,
                        method.Name,
                        $"parameter '{parameter.Name}' type '{parameter.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}' is not supported"));
                    valid = false;
                }
            }

            if (parameters.Length == 0 || parameters.Length > 2)
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0003",
                    methodLocation,
                    method.Name,
                    "operation methods must accept exactly one request parameter and an optional trailing CancellationToken"));
                return false;
            }

            bool firstIsCt = known.CancellationTokenType is not null
                && SymbolEqualityComparer.Default.Equals(parameters[0].Type, known.CancellationTokenType);

            if (firstIsCt)
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0003",
                    EquatableLocation.Create(GetPrimaryLocation(parameters[0], null)),
                    method.Name,
                    "first parameter must be the request payload, not CancellationToken"));
                return false;
            }

            if (parameters.Length == 2)
            {
                bool secondIsCt = known.CancellationTokenType is not null
                    && SymbolEqualityComparer.Default.Equals(parameters[1].Type, known.CancellationTokenType);

                if (!secondIsCt)
                {
                    diagnostics.Add(CreateDiagnostic(
                        "FS0003",
                        EquatableLocation.Create(GetPrimaryLocation(parameters[1], null)),
                        method.Name,
                        "only an optional trailing CancellationToken second parameter is supported"));
                    return false;
                }

                hasCancellationToken = true;
            }

            if (!valid)
            {
                return false;
            }

            inputParamType = parameters[0].Type;
            return true;
        }

        private static bool TryValidateReturnType(
            IMethodSymbol method,
            EquatableLocation methodLocation,
            KnownSymbols known,
            List<DiagnosticSpec> diagnostics,
            out ITypeSymbol? returnInnerType)
        {
            returnInnerType = null;

            if (method.ReturnsVoid || method.ReturnType is not INamedTypeSymbol returnNamed || !returnNamed.IsGenericType)
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0003",
                    methodLocation,
                    method.Name,
                    "return type must be Task<T> or ValueTask<T>"));
                return false;
            }

            bool isTask = known.TaskGenericType is not null
                && SymbolEqualityComparer.Default.Equals(returnNamed.OriginalDefinition, known.TaskGenericType);
            bool isValueTask = known.ValueTaskGenericType is not null
                && SymbolEqualityComparer.Default.Equals(returnNamed.OriginalDefinition, known.ValueTaskGenericType);

            if ((!isTask && !isValueTask) || returnNamed.TypeArguments.Length != 1)
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0003",
                    methodLocation,
                    method.Name,
                    "return type must be Task<T> or ValueTask<T>"));
                return false;
            }

            ITypeSymbol inner = returnNamed.TypeArguments[0];
            if (inner.IsRefLikeType
                || inner.TypeKind == TypeKind.Pointer
                || inner.TypeKind == TypeKind.FunctionPointer
                || (known.StreamType is not null && SymbolEqualityComparer.Default.Equals(inner, known.StreamType)))
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0003",
                    methodLocation,
                    method.Name,
                    $"return payload type '{inner.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}' is not supported"));
                return false;
            }

            returnInnerType = inner;
            return true;
        }

        private static bool ValidateContextRegistration(
            INamedTypeSymbol contextType,
            ITypeSymbol wireType,
            bool hasOverrideName,
            string? overridePropertyName,
            string operationName,
            EquatableLocation location,
            KnownSymbols known,
            List<DiagnosticSpec> diagnostics,
            out string propertyName)
        {
            propertyName = string.Empty;

            if (hasOverrideName)
            {
                if (string.IsNullOrWhiteSpace(overridePropertyName) || !SyntaxFacts.IsValidIdentifier(overridePropertyName!))
                {
                    diagnostics.Add(CreateDiagnostic(
                        "FS0005",
                        location,
                        operationName,
                        $"TypeInfoPropertyName override '{overridePropertyName}' is not a valid identifier"));
                    return false;
                }
            }

            bool foundXmlSerializable = false;
            string? attrPropertyName = null;

            if (known.XmlSerializableGenericAttribute is not null)
            {
                foreach (AttributeData attribute in contextType.GetAttributes())
                {
                    INamedTypeSymbol? attrClass = attribute.AttributeClass;
                    if (attrClass is not null
                        && attrClass.IsGenericType
                        && SymbolEqualityComparer.Default.Equals(attrClass.OriginalDefinition, known.XmlSerializableGenericAttribute)
                        && attrClass.TypeArguments.Length == 1
                        && SymbolEqualityComparer.Default.Equals(attrClass.TypeArguments[0], wireType))
                    {
                        foundXmlSerializable = true;
                        foreach (KeyValuePair<string, TypedConstant> namedArg in attribute.NamedArguments)
                        {
                            if (string.Equals(namedArg.Key, "TypeInfoPropertyName", StringComparison.Ordinal))
                            {
                                attrPropertyName = namedArg.Value.Value as string;
                            }
                        }

                        break;
                    }
                }
            }

            bool isPartial = IsPartialType(contextType);
            string resolvedName = hasOverrideName
                ? overridePropertyName!
                : (!string.IsNullOrWhiteSpace(attrPropertyName) ? attrPropertyName! : wireType.Name);

            if (hasOverrideName && !string.IsNullOrWhiteSpace(attrPropertyName) && !string.Equals(overridePropertyName, attrPropertyName, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0005",
                    location,
                    operationName,
                    $"TypeInfoPropertyName '{overridePropertyName}' conflicts with [XmlSerializable<{wireType.Name}>(TypeInfoPropertyName = \"{attrPropertyName}\")] on '{contextType.Name}'"));
                return false;
            }

            if (foundXmlSerializable && isPartial)
            {
                if (!SyntaxFacts.IsValidIdentifier(resolvedName))
                {
                    diagnostics.Add(CreateDiagnostic(
                        "FS0005",
                        location,
                        operationName,
                        $"resolved XmlTypeInfo property name '{resolvedName}' on '{contextType.Name}' is not a valid C# identifier"));
                    return false;
                }

                propertyName = resolvedName;
                return true;
            }

            // Fallback: allow authored or external-assembly XmlSerializerContext declaring Default and XmlTypeInfo<T> explicitly
            IPropertySymbol? defaultProp = FindProperty(contextType, "Default");
            IPropertySymbol? typeInfoProp = FindProperty(contextType, resolvedName);
            if (defaultProp is not null && defaultProp.IsStatic && typeInfoProp is not null && !typeInfoProp.IsStatic)
            {
                if (!IsMemberAccessibleFromGeneratedCode(defaultProp, known.Compilation)
                    || !IsMemberAccessibleFromGeneratedCode(typeInfoProp, known.Compilation))
                {
                    diagnostics.Add(CreateDiagnostic(
                        "FS0007",
                        location,
                        $"{contextType.Name}.{resolvedName}",
                        "XmlSerializerContext Default or type info property is inaccessible"));
                    return false;
                }

                propertyName = resolvedName;
                return true;
            }

            if (!isPartial && contextType.DeclaringSyntaxReferences.Length > 0)
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0005",
                    location,
                    operationName,
                    $"XmlSerializerContext '{contextType.Name}' must be declared 'partial' with [XmlSerializable<{wireType.Name}>]"));
                return false;
            }

            diagnostics.Add(CreateDiagnostic(
                "FS0005",
                location,
                operationName,
                $"XmlSerializerContext '{contextType.Name}' is missing [XmlSerializable<{wireType.Name}>]"));
            return false;
        }

        private static bool ValidateRequestMemberMapping(
            ITypeSymbol requestWireSymbol,
            bool hasRequestMemberName,
            string? requestMemberName,
            ITypeSymbol inputParamType,
            string operationName,
            EquatableLocation opWireLocation,
            KnownSymbols known,
            List<DiagnosticSpec> diagnostics)
        {
            if (!hasRequestMemberName || requestMemberName is null)
            {
                if (!IsImplicitlyConvertible(requestWireSymbol, inputParamType, known.Compilation))
                {
                    diagnostics.Add(CreateDiagnostic(
                        "FS0004",
                        opWireLocation,
                        operationName,
                        $"RequestType '{requestWireSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}' is not convertible to parameter type '{inputParamType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}'; specify RequestMemberName for wrapped DTOs"));
                    return false;
                }

                return true;
            }

            if (string.IsNullOrWhiteSpace(requestMemberName))
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0004",
                    opWireLocation,
                    operationName,
                    "RequestMemberName cannot be empty or whitespace"));
                return false;
            }

            IPropertySymbol? prop = FindProperty(requestWireSymbol, requestMemberName);
            if (prop is null || prop.IsStatic || prop.GetMethod is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0004",
                    opWireLocation,
                    operationName,
                    $"RequestType '{requestWireSymbol.Name}' does not define a readable instance property '{requestMemberName}'"));
                return false;
            }

            if (!IsMemberAccessibleFromGeneratedCode(prop, known.Compilation)
                || !IsMemberAccessibleFromGeneratedCode(prop.GetMethod, known.Compilation))
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0007",
                    opWireLocation,
                    $"{requestWireSymbol.Name}.{requestMemberName}",
                    "request wrapper property getter is inaccessible"));
                return false;
            }

            if (!IsImplicitlyConvertible(prop.Type, inputParamType, known.Compilation))
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0004",
                    opWireLocation,
                    operationName,
                    $"property '{requestWireSymbol.Name}.{requestMemberName}' of type '{prop.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}' is not convertible to parameter type '{inputParamType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}'"));
                return false;
            }

            return true;
        }

        private static bool ValidateResponseMemberMapping(
            ITypeSymbol responseWireSymbol,
            bool hasResponseMemberName,
            string? responseMemberName,
            ITypeSymbol returnInnerType,
            string operationName,
            EquatableLocation opWireLocation,
            KnownSymbols known,
            List<DiagnosticSpec> diagnostics)
        {
            if (!hasResponseMemberName || responseMemberName is null)
            {
                if (!IsImplicitlyConvertible(returnInnerType, responseWireSymbol, known.Compilation))
                {
                    diagnostics.Add(CreateDiagnostic(
                        "FS0004",
                        opWireLocation,
                        operationName,
                        $"method return payload '{returnInnerType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}' is not convertible to ResponseType '{responseWireSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}'; specify ResponseMemberName for wrapped DTOs"));
                    return false;
                }

                return true;
            }

            if (string.IsNullOrWhiteSpace(responseMemberName))
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0004",
                    opWireLocation,
                    operationName,
                    "ResponseMemberName cannot be empty or whitespace"));
                return false;
            }

            IPropertySymbol? prop = FindProperty(responseWireSymbol, responseMemberName);
            if (prop is null || prop.IsStatic || prop.SetMethod is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0004",
                    opWireLocation,
                    operationName,
                    $"ResponseType '{responseWireSymbol.Name}' does not define a writable instance property '{responseMemberName}'"));
                return false;
            }

            if (!IsMemberAccessibleFromGeneratedCode(prop, known.Compilation)
                || !IsMemberAccessibleFromGeneratedCode(prop.SetMethod, known.Compilation))
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0007",
                    opWireLocation,
                    $"{responseWireSymbol.Name}.{responseMemberName}",
                    "response wrapper property setter is inaccessible"));
                return false;
            }

            if (!IsImplicitlyConvertible(returnInnerType, prop.Type, known.Compilation))
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0004",
                    opWireLocation,
                    operationName,
                    $"method return payload '{returnInnerType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}' is not convertible to property '{responseWireSymbol.Name}.{responseMemberName}' of type '{prop.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}'"));
                return false;
            }

            return true;
        }

        private static bool ValidateWireDtoShapeAndQName(
            ITypeSymbol wireTypeSymbol,
            string? explicitOperationElementName,
            bool isRequest,
            bool requireParameterlessCtor,
            EquatableLocation opWireLocation,
            KnownSymbols known,
            List<DiagnosticSpec> diagnostics,
            out string namespaceUri,
            out string localName)
        {
            namespaceUri = string.Empty;
            localName = string.Empty;

            string roleLabel = isRequest ? "RequestType" : "ResponseType";
            string typeDisplay = wireTypeSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

            if (!IsTypeAccessibleFromGeneratedCode(wireTypeSymbol, known.Compilation))
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0007",
                    opWireLocation,
                    typeDisplay,
                    $"{roleLabel} wire DTO is inaccessible from generated code"));
                return false;
            }

            if (wireTypeSymbol is not INamedTypeSymbol namedWireType || IsUnsupportedTypeShape(wireTypeSymbol, known))
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0006",
                    opWireLocation,
                    typeDisplay,
                    $"{roleLabel} has an unsupported wire schema shape"));
                return false;
            }

            if (namedWireType.IsUnboundGenericType || namedWireType.TypeParameters.Length > 0)
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0006",
                    opWireLocation,
                    typeDisplay,
                    $"{roleLabel} cannot be an open generic type"));
                return false;
            }

            if (requireParameterlessCtor)
            {
                if (!HasAccessibleParameterlessConstructor(namedWireType, known.Compilation, out bool foundInaccessibleCtor))
                {
                    if (foundInaccessibleCtor)
                    {
                        diagnostics.Add(CreateDiagnostic(
                            "FS0007",
                            opWireLocation,
                            typeDisplay,
                            $"{roleLabel} parameterless constructor is inaccessible"));
                    }
                    else
                    {
                        diagnostics.Add(CreateDiagnostic(
                            "FS0006",
                            opWireLocation,
                            typeDisplay,
                            $"{roleLabel} must expose a parameterless constructor"));
                    }

                    return false;
                }
            }

            AttributeData? xmlRootAttr = GetAttribute(namedWireType, known.XmlRootAttribute);
            if (xmlRootAttr is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0006",
                    opWireLocation,
                    typeDisplay,
                    $"{roleLabel} must declare [XmlRoot(ElementName = ..., Namespace = ...)]"));
                return false;
            }

            string? xmlRootElementName = null;
            bool hasXmlRootElementName = false;
            string? xmlRootNamespace = null;
            bool hasXmlRootNamespace = false;

            foreach (KeyValuePair<string, TypedConstant> namedArg in xmlRootAttr.NamedArguments)
            {
                if (string.Equals(namedArg.Key, "ElementName", StringComparison.Ordinal))
                {
                    hasXmlRootElementName = true;
                    xmlRootElementName = namedArg.Value.Value as string;
                }
                else if (string.Equals(namedArg.Key, "Namespace", StringComparison.Ordinal))
                {
                    hasXmlRootNamespace = true;
                    xmlRootNamespace = namedArg.Value.Value as string;
                }
            }

            if (!hasXmlRootNamespace || xmlRootNamespace is null || !XmlNameValidator.IsValidOptionalNamespaceUri(xmlRootNamespace))
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0006",
                    EquatableLocation.Create(GetAttributeLocation(xmlRootAttr, opWireLocation)),
                    typeDisplay,
                    "[XmlRoot] must specify a valid Namespace without whitespace or control characters"));
                return false;
            }

            string effectiveXmlRootLocalName = hasXmlRootElementName
                ? (xmlRootElementName ?? string.Empty)
                : namedWireType.Name;

            if (!XmlNameValidator.IsValidLocalName(effectiveXmlRootLocalName))
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0006",
                    EquatableLocation.Create(GetAttributeLocation(xmlRootAttr, opWireLocation)),
                    typeDisplay,
                    $"[XmlRoot] ElementName '{effectiveXmlRootLocalName}' is not a valid XML NCName"));
                return false;
            }

            if (explicitOperationElementName is not null && !string.Equals(explicitOperationElementName, effectiveXmlRootLocalName, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    "FS0006",
                    opWireLocation,
                    typeDisplay,
                    $"operation element name '{explicitOperationElementName}' conflicts with [XmlRoot(ElementName = \"{effectiveXmlRootLocalName}\")]"));
                return false;
            }

            foreach (ISymbol member in namedWireType.GetMembers())
            {
                if (member is not IPropertySymbol prop)
                {
                    continue;
                }

                AttributeData? elemAttr = GetAttribute(prop, known.XmlElementAttribute);
                if (elemAttr is null && known.XmlElementGenericAttribute is not null)
                {
                    foreach (AttributeData attr in prop.GetAttributes())
                    {
                        if (attr.AttributeClass is { IsGenericType: true } genericAttr
                            && SymbolEqualityComparer.Default.Equals(genericAttr.OriginalDefinition, known.XmlElementGenericAttribute))
                        {
                            elemAttr = attr;
                            break;
                        }
                    }
                }

                if (elemAttr is not null)
                {
                    foreach (KeyValuePair<string, TypedConstant> namedArg in elemAttr.NamedArguments)
                    {
                        if (string.Equals(namedArg.Key, "ElementName", StringComparison.Ordinal)
                            && namedArg.Value.Value is string memberElemName
                            && !XmlNameValidator.IsValidLocalName(memberElemName))
                        {
                            diagnostics.Add(CreateDiagnostic(
                                "FS0006",
                                EquatableLocation.Create(GetAttributeLocation(elemAttr, opWireLocation)),
                                $"{namedWireType.Name}.{prop.Name}",
                                $"[XmlElement] ElementName '{memberElemName}' is not a valid XML NCName"));
                            return false;
                        }

                        if (string.Equals(namedArg.Key, "Namespace", StringComparison.Ordinal)
                            && namedArg.Value.Value is string memberNs
                            && !XmlNameValidator.IsValidOptionalNamespaceUri(memberNs))
                        {
                            diagnostics.Add(CreateDiagnostic(
                                "FS0006",
                                EquatableLocation.Create(GetAttributeLocation(elemAttr, opWireLocation)),
                                $"{namedWireType.Name}.{prop.Name}",
                                "[XmlElement] Namespace contains invalid whitespace or control characters"));
                            return false;
                        }
                    }
                }
            }

            namespaceUri = xmlRootNamespace;
            localName = effectiveXmlRootLocalName;
            return true;
        }

        #endregion

        private readonly struct KnownSymbols
        {

            #region Properties

            internal INamedTypeSymbol? CancellationTokenType { get; }

            internal Compilation Compilation { get; }

            internal INamedTypeSymbol? EndpointRouteBuilder { get; }

            internal INamedTypeSymbol? SoapOperationAttribute { get; }

            internal INamedTypeSymbol? SoapServiceAttribute { get; }

            internal INamedTypeSymbol? SoapWireContractAttribute { get; }

            internal INamedTypeSymbol? StreamType { get; }

            internal INamedTypeSymbol? TaskGenericType { get; }

            internal INamedTypeSymbol? ValueTaskGenericType { get; }

            internal INamedTypeSymbol? XmlElementAttribute { get; }

            internal INamedTypeSymbol? XmlElementGenericAttribute { get; }

            internal INamedTypeSymbol? XmlRootAttribute { get; }

            internal INamedTypeSymbol? XmlSerializableGenericAttribute { get; }

            internal INamedTypeSymbol? XmlSerializerContext { get; }

            #endregion

            #region Constructors

            private KnownSymbols(Compilation compilation)
            {
                Compilation = compilation;
                CancellationTokenType = compilation.GetTypeByMetadataName(CancellationTokenMetadataName);
                EndpointRouteBuilder = compilation.GetTypeByMetadataName(EndpointRouteBuilderMetadataName);
                SoapOperationAttribute = compilation.GetTypeByMetadataName(SoapOperationAttributeMetadataName);
                SoapServiceAttribute = compilation.GetTypeByMetadataName(SoapServiceAttributeMetadataName);
                SoapWireContractAttribute = compilation.GetTypeByMetadataName(SoapWireContractAttributeMetadataName);
                StreamType = compilation.GetTypeByMetadataName(StreamMetadataName);
                TaskGenericType = compilation.GetTypeByMetadataName(TaskGenericMetadataName);
                ValueTaskGenericType = compilation.GetTypeByMetadataName(ValueTaskGenericMetadataName);
                XmlElementAttribute = compilation.GetTypeByMetadataName(XmlElementAttributeMetadataName);
                XmlElementGenericAttribute = compilation.GetTypeByMetadataName(XmlElementGenericAttributeMetadataName);
                XmlRootAttribute = compilation.GetTypeByMetadataName(XmlRootAttributeMetadataName);
                XmlSerializableGenericAttribute = compilation.GetTypeByMetadataName(XmlSerializableGenericAttributeMetadataName);
                XmlSerializerContext = compilation.GetTypeByMetadataName(XmlSerializerContextMetadataName);
            }

            #endregion

            #region Internal Methods

            internal static KnownSymbols Create(Compilation compilation)
            {
                return new KnownSymbols(compilation);
            }

            #endregion

        }

        private readonly struct OperationExtractionOutcome
        {

            #region Properties

            internal OperationGenerationModel? Model { get; }

            internal EquatableLocation OperationLocation { get; }

            #endregion

            #region Constructors

            internal OperationExtractionOutcome(OperationGenerationModel? model, EquatableLocation operationLocation)
            {
                Model = model;
                OperationLocation = operationLocation;
            }

            #endregion

        }

    }

}
