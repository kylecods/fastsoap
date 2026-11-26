using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using CloudNimble.Text.Xml;
using CloudNimble.Text.Xml.Serialization;
using CloudNimble.XmlEssentials.SourceGeneration;
using FastSoap.Abstractions;
using FastSoap.Generators;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;

namespace FastSoap.GeneratorTests
{

    /// <summary>
    /// Shared Roslyn compilation and generator driver test harness for <see cref="FastSoapContractGenerator"/>.
    /// </summary>
    internal static class GeneratorTestHarness
    {

        #region Fields

        /// <summary>
        /// Minimal <c>FastSoap.AspNetCore</c> endpoint builder surface matching Plan section 3.3,
        /// used inside test compilations until hosting implementation is present.
        /// </summary>
        internal const string AspNetCoreBuilderStubSource = """
            using System;
            using CloudNimble.Text.Xml.Serialization;
            using FastSoap.Abstractions;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            namespace FastSoap.AspNetCore
            {

                public sealed class SoapEndpointBuilder
                {
                    public SoapEndpointBuilder MapOperation<TRequest, TResponse>(
                        SoapOperationDescriptor descriptor,
                        XmlTypeInfo<TRequest> requestType,
                        XmlTypeInfo<TResponse> responseType,
                        SoapOperationHandler<TRequest, TResponse> handler)
                    {
                        return this;
                    }
                }

                public static class SoapEndpointRouteBuilderExtensions
                {
                    public static IEndpointConventionBuilder MapSoap(
                        this IEndpointRouteBuilder endpoints,
                        string pattern,
                        Action<SoapEndpointBuilder> configure)
                    {
                        ArgumentNullException.ThrowIfNull(endpoints);
                        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
                        ArgumentNullException.ThrowIfNull(configure);

                        return new StubEndpointConventionBuilder();
                    }
                }

                internal sealed class StubEndpointConventionBuilder : IEndpointConventionBuilder
                {
                    public void Add(Action<EndpointBuilder> convention)
                    {
                    }
                }

            }
            """;

        private static readonly Lazy<ImmutableArray<MetadataReference>> CachedMetadataReferences =
            new Lazy<ImmutableArray<MetadataReference>>(BuildMetadataReferences, LazyThreadSafetyMode.ExecutionAndPublication);

        #endregion

        #region Internal Methods

        /// <summary>
        /// Creates a <see cref="CSharpCompilation"/> with FastSoap, XmlEssentials, and ASP.NET Core references.
        /// </summary>
        /// <param name="sources">Source texts to compile (ordered and paired with file paths).</param>
        /// <param name="assemblyName">Target assembly name.</param>
        /// <returns>The constructed <see cref="CSharpCompilation"/>.</returns>
        internal static CSharpCompilation CreateCompilation(
            IReadOnlyList<(string FilePath, string Source)> sources,
            string assemblyName = "FastSoap.GeneratorTestAssembly")
        {
            CSharpParseOptions parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
            SyntaxTree[] trees = new SyntaxTree[sources.Count];
            for (int index = 0; index < sources.Count; index++)
            {
                trees[index] = CSharpSyntaxTree.ParseText(
                    sources[index].Source,
                    parseOptions,
                    path: sources[index].FilePath);
            }

            return CSharpCompilation.Create(
                assemblyName: assemblyName,
                syntaxTrees: trees,
                references: CachedMetadataReferences.Value,
                options: new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable));
        }

        /// <summary>
        /// Creates a <see cref="CSharpGeneratorDriver"/> configured with <see cref="FastSoapContractGenerator"/>
        /// and incremental step tracking enabled.
        /// </summary>
        /// <param name="includeXmlEssentialsGenerator">Whether to also include <see cref="SerializerGenerator"/> in the same compilation.</param>
        /// <returns>The configured generator driver.</returns>
        internal static GeneratorDriver CreateDriver(bool includeXmlEssentialsGenerator = false)
        {
            FastSoapContractGenerator fastSoapGenerator = new FastSoapContractGenerator();
            ISourceGenerator[] generators = includeXmlEssentialsGenerator
                ? [fastSoapGenerator.AsSourceGenerator(), new SerializerGenerator().AsSourceGenerator()]
                : [fastSoapGenerator.AsSourceGenerator()];

            CSharpParseOptions parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
            GeneratorDriverOptions driverOptions = new GeneratorDriverOptions(
                disabledOutputs: IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true);

            return CSharpGeneratorDriver.Create(
                generators: generators,
                parseOptions: parseOptions,
                driverOptions: driverOptions);
        }

        /// <summary>
        /// Runs <see cref="FastSoapContractGenerator"/> (and optionally XmlEssentials's generator) on the specified sources.
        /// </summary>
        /// <param name="sources">Input source files.</param>
        /// <param name="includeXmlEssentialsGenerator">Whether to run XmlEssentials's generator in the same compilation.</param>
        /// <param name="includeAspNetCoreStub">Whether to include the minimal <c>MapSoap</c> stub in the compilation.</param>
        /// <returns>The run result containing generated trees, diagnostics, and updated compilation.</returns>
        internal static GeneratorRunOutcome RunGenerator(
            IReadOnlyList<(string FilePath, string Source)> sources,
            bool includeXmlEssentialsGenerator = false,
            bool includeAspNetCoreStub = true)
        {
            List<(string FilePath, string Source)> allSources = new List<(string FilePath, string Source)>(sources.Count + 1);
            allSources.AddRange(sources);
            if (includeAspNetCoreStub)
            {
                allSources.Add(("AspNetCoreStub.cs", AspNetCoreBuilderStubSource));
            }

            CSharpCompilation compilation = CreateCompilation(allSources);
            GeneratorDriver driver = CreateDriver(includeXmlEssentialsGenerator);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out ImmutableArray<Diagnostic> generatorDiagnostics);

            GeneratorDriverRunResult runResult = driver.GetRunResult();
            ImmutableArray<Diagnostic> compilationDiagnostics = outputCompilation.GetDiagnostics();

            return new GeneratorRunOutcome(
                driver,
                runResult,
                outputCompilation,
                generatorDiagnostics,
                compilationDiagnostics);
        }

        #endregion

        #region Private Methods

        private static ImmutableArray<MetadataReference> BuildMetadataReferences()
        {
            Dictionary<string, MetadataReference> references = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);

            string? tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            if (!string.IsNullOrWhiteSpace(tpa))
            {
                string[] paths = tpa.Split(Path.PathSeparator);
                for (int index = 0; index < paths.Length; index++)
                {
                    string path = paths[index];
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    {
                        continue;
                    }

                    string fileName = Path.GetFileName(path);
                    if (fileName.StartsWith("FastSoap.Generators", StringComparison.OrdinalIgnoreCase)
                        || fileName.StartsWith("CloudNimble.XmlEssentials.SourceGeneration", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    references[path] = MetadataReference.CreateFromFile(path);
                }
            }

            Assembly[] explicitAssemblies =
            [
                typeof(SoapServiceAttribute).Assembly,
                typeof(Utf8XmlReader).Assembly,
                typeof(XmlSerializerContext).Assembly,
                typeof(WebApplication).Assembly,
                typeof(IEndpointRouteBuilder).Assembly,
                typeof(IEndpointConventionBuilder).Assembly,
                typeof(ServiceProviderServiceExtensions).Assembly,
                typeof(ClaimsPrincipal).Assembly,
            ];

            for (int index = 0; index < explicitAssemblies.Length; index++)
            {
                string location = explicitAssemblies[index].Location;
                if (!string.IsNullOrWhiteSpace(location) && File.Exists(location))
                {
                    references[location] = MetadataReference.CreateFromFile(location);
                }
            }

            return references.Values.ToImmutableArray();
        }

        #endregion

    }

    /// <summary>
    /// Holds the complete output of running the incremental generator driver on a compilation.
    /// </summary>
    internal sealed class GeneratorRunOutcome
    {

        #region Properties

        /// <summary>
        /// Gets all diagnostics produced by the final output compilation (excluding hidden/info).
        /// </summary>
        public ImmutableArray<Diagnostic> CompilationDiagnostics { get; }

        /// <summary>
        /// Gets the updated <see cref="GeneratorDriver"/> for subsequent incremental runs.
        /// </summary>
        public GeneratorDriver Driver { get; }

        /// <summary>
        /// Gets all diagnostics emitted directly by the source generator(s).
        /// </summary>
        public ImmutableArray<Diagnostic> GeneratorDiagnostics { get; }

        /// <summary>
        /// Gets the updated <see cref="Compilation"/> including generated syntax trees.
        /// </summary>
        public Compilation OutputCompilation { get; }

        /// <summary>
        /// Gets the detailed <see cref="GeneratorDriverRunResult"/>.
        /// </summary>
        public GeneratorDriverRunResult RunResult { get; }

        #endregion

        #region Constructors

        public GeneratorRunOutcome(
            GeneratorDriver driver,
            GeneratorDriverRunResult runResult,
            Compilation outputCompilation,
            ImmutableArray<Diagnostic> generatorDiagnostics,
            ImmutableArray<Diagnostic> compilationDiagnostics)
        {
            Driver = driver;
            RunResult = runResult;
            OutputCompilation = outputCompilation;
            GeneratorDiagnostics = generatorDiagnostics;
            CompilationDiagnostics = compilationDiagnostics;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Retrieves the generated source text for an exact hint name from the first generator result.
        /// </summary>
        /// <param name="hintName">The exact hint name.</param>
        /// <returns>The generated source text normalized to LF line endings.</returns>
        public string GetGeneratedSource(string hintName)
        {
            GeneratorRunResult fastSoapResult = RunResult.Results[0];
            GeneratedSourceResult match = fastSoapResult.GeneratedSources.Single(
                s => string.Equals(s.HintName, hintName, StringComparison.Ordinal));

            return match.SourceText.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        #endregion

    }

}
