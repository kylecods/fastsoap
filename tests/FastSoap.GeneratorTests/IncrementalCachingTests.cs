using System.Collections.Immutable;
using System.Linq;
using FastSoap.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace FastSoap.GeneratorTests
{

    /// <summary>
    /// Verifies Roslyn incremental caching and invalidation behavior across no-op, unrelated, trivia, and contract edits.
    /// </summary>
    public sealed class IncrementalCachingTests
    {

        #region Fields

        private const string ContractSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using CloudNimble.Text.Xml.Serialization;
            using FastSoap.Abstractions;

            namespace Echo
            {

                [XmlRoot(ElementName = "Echo", Namespace = "urn:echo")]
                public sealed class EchoRequest
                {
                    [XmlElement(ElementName = "Message", Namespace = "urn:echo")]
                    public required string Message { get; init; }
                }

                [XmlRoot(ElementName = "EchoResponse", Namespace = "urn:echo")]
                public sealed class EchoResponse
                {
                    [XmlElement(ElementName = "EchoResult", Namespace = "urn:echo")]
                    public required string EchoResult { get; init; }
                }

                [XmlSerializable<EchoRequest>]
                [XmlSerializable<EchoResponse>]
                public partial class EchoXmlContext : XmlSerializerContext
                {
                }

                [SoapService(Namespace = "urn:echo")]
                [SoapWireContract(typeof(EchoXmlContext))]
                public interface IEchoService
                {
                    [SoapOperation(Action = "urn:echo/IEchoService/Echo")]
                    [SoapWireContract(RequestType = typeof(EchoRequest), ResponseType = typeof(EchoResponse))]
                    Task<EchoResponse> EchoAsync(EchoRequest request, CancellationToken cancellationToken);
                }

            }
            """;

        private const string HelperSource = """
            namespace Echo
            {
                public static class Helper
                {
                    public static int GetVersion()
                    {
                        return 1;
                    }
                }
            }
            """;

        #endregion

        #region Public Methods

        [Fact]
        public void ContractEdit_MarksStepsModifiedAndUpdatesOutput()
        {
            CSharpCompilation compilation1 = GeneratorTestHarness.CreateCompilation(
                [("EchoService.cs", ContractSource), ("Helper.cs", HelperSource), ("AspNetCoreStub.cs", GeneratorTestHarness.AspNetCoreBuilderStubSource)]);

            GeneratorDriver driver = GeneratorTestHarness.CreateDriver();
            driver = driver.RunGenerators(compilation1, TestContext.Current.CancellationToken);

            SyntaxTree contractTree = compilation1.SyntaxTrees.Single(static t => t.FilePath == "EchoService.cs");
            Microsoft.CodeAnalysis.Text.SourceText originalText = contractTree.GetText(TestContext.Current.CancellationToken);
            SyntaxTree updatedTree = contractTree.WithChangedText(
                originalText.Replace(
                    0,
                    originalText.Length,
                    ContractSource.Replace("urn:echo/IEchoService/Echo", "urn:echo/IEchoService/EchoV2", System.StringComparison.Ordinal)));

            CSharpCompilation compilation2 = compilation1.ReplaceSyntaxTree(contractTree, updatedTree);
            driver = driver.RunGenerators(compilation2, TestContext.Current.CancellationToken);
            GeneratorRunResult run2 = driver.GetRunResult().Results[0];

            AssertAllOutputsHaveReason(run2, FastSoapContractGenerator.DiscoverContractsTrackingName, IncrementalStepRunReason.Modified);
            AssertAllOutputsHaveReason(run2, FastSoapContractGenerator.ExtractGenerationModelsTrackingName, IncrementalStepRunReason.Modified);
            Assert.Contains(
                "\"urn:echo/IEchoService/EchoV2\"",
                run2.GeneratedSources.Single(static s => s.HintName == "Echo.IEchoService.SoapContract.g.cs").SourceText.ToString(),
                System.StringComparison.Ordinal);
        }

        [Fact]
        public void NoOpEdit_CachesAllTrackedSteps()
        {
            CSharpCompilation compilation = GeneratorTestHarness.CreateCompilation(
                [("EchoService.cs", ContractSource), ("Helper.cs", HelperSource), ("AspNetCoreStub.cs", GeneratorTestHarness.AspNetCoreBuilderStubSource)]);

            GeneratorDriver driver = GeneratorTestHarness.CreateDriver();
            driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
            GeneratorRunResult run1 = driver.GetRunResult().Results[0];

            AssertAllOutputsHaveReason(run1, FastSoapContractGenerator.DiscoverContractsTrackingName, IncrementalStepRunReason.New);

            driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
            GeneratorRunResult run2 = driver.GetRunResult().Results[0];

            AssertAllOutputsHaveReason(run2, FastSoapContractGenerator.DiscoverContractsTrackingName, IncrementalStepRunReason.Cached);
            AssertAllOutputsHaveReason(run2, FastSoapContractGenerator.ExtractGenerationModelsTrackingName, IncrementalStepRunReason.Cached);
            AssertAllOutputsHaveReason(run2, FastSoapContractGenerator.CollectConflictsTrackingName, IncrementalStepRunReason.Cached);
        }

        [Fact]
        public void TriviaShiftInContractFile_KeepsGenerationModelUnchangedAndOutputCached()
        {
            CSharpCompilation compilation1 = GeneratorTestHarness.CreateCompilation(
                [("EchoService.cs", ContractSource), ("Helper.cs", HelperSource), ("AspNetCoreStub.cs", GeneratorTestHarness.AspNetCoreBuilderStubSource)]);

            GeneratorDriver driver = GeneratorTestHarness.CreateDriver();
            driver = driver.RunGenerators(compilation1, TestContext.Current.CancellationToken);

            SyntaxTree contractTree = compilation1.SyntaxTrees.Single(static t => t.FilePath == "EchoService.cs");
            string shiftedSource = "// Comment shifting line numbers\n\n" + ContractSource;
            SyntaxTree shiftedTree = CSharpSyntaxTree.ParseText(
                shiftedSource,
                (CSharpParseOptions)contractTree.Options,
                path: "EchoService.cs",
                cancellationToken: TestContext.Current.CancellationToken);

            CSharpCompilation compilation2 = compilation1.ReplaceSyntaxTree(contractTree, shiftedTree);
            driver = driver.RunGenerators(compilation2, TestContext.Current.CancellationToken);
            GeneratorRunResult run2 = driver.GetRunResult().Results[0];

            AssertAllOutputsHaveReason(run2, FastSoapContractGenerator.DiscoverContractsTrackingName, IncrementalStepRunReason.Modified);
            AssertAllOutputsHaveReason(run2, FastSoapContractGenerator.ExtractGenerationModelsTrackingName, IncrementalStepRunReason.Cached);
            AssertAllOutputsHaveReason(run2, FastSoapContractGenerator.CollectConflictsTrackingName, IncrementalStepRunReason.Unchanged);
        }

        [Fact]
        public void UnrelatedFileEdit_CachesModelsAndOutputs()
        {
            CSharpCompilation compilation1 = GeneratorTestHarness.CreateCompilation(
                [("EchoService.cs", ContractSource), ("Helper.cs", HelperSource), ("AspNetCoreStub.cs", GeneratorTestHarness.AspNetCoreBuilderStubSource)]);

            GeneratorDriver driver = GeneratorTestHarness.CreateDriver();
            driver = driver.RunGenerators(compilation1, TestContext.Current.CancellationToken);

            SyntaxTree helperTree = compilation1.SyntaxTrees.Single(static t => t.FilePath == "Helper.cs");
            string updatedHelper = HelperSource.Replace("return 1;", "return 42;", System.StringComparison.Ordinal);
            SyntaxTree updatedHelperTree = CSharpSyntaxTree.ParseText(
                updatedHelper,
                (CSharpParseOptions)helperTree.Options,
                path: "Helper.cs",
                cancellationToken: TestContext.Current.CancellationToken);

            CSharpCompilation compilation2 = compilation1.ReplaceSyntaxTree(helperTree, updatedHelperTree);
            driver = driver.RunGenerators(compilation2, TestContext.Current.CancellationToken);
            GeneratorRunResult run2 = driver.GetRunResult().Results[0];

            AssertAllOutputsHaveReason(run2, FastSoapContractGenerator.DiscoverContractsTrackingName, IncrementalStepRunReason.Cached);
            AssertAllOutputsHaveReason(run2, FastSoapContractGenerator.ExtractGenerationModelsTrackingName, IncrementalStepRunReason.Cached);
            AssertAllOutputsHaveReason(run2, FastSoapContractGenerator.CollectConflictsTrackingName, IncrementalStepRunReason.Cached);
        }

        #endregion

        #region Private Methods

        private static void AssertAllOutputsHaveReason(
            GeneratorRunResult runResult,
            string trackingName,
            IncrementalStepRunReason expectedReason)
        {
            Assert.True(runResult.TrackedSteps.TryGetValue(trackingName, out ImmutableArray<IncrementalGeneratorRunStep> steps));
            Assert.NotEmpty(steps);

            foreach (IncrementalGeneratorRunStep step in steps)
            {
                Assert.NotEmpty(step.Outputs);
                foreach ((object Value, IncrementalStepRunReason Reason) output in step.Outputs)
                {
                    Assert.Equal(expectedReason, output.Reason);
                }
            }
        }

        #endregion

    }

}
