using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace FastSoap.GeneratorTests
{

    /// <summary>
    /// Diagnostic tests covering every FastSoap contract generator error <c>FS0001</c> through <c>FS0008</c>.
    /// </summary>
    public sealed class DiagnosticTests
    {

        #region Fields

        private const string SharedDtoAndContextSource = """
            using CloudNimble.Text.Xml.Serialization;

            namespace TestContracts
            {
                [XmlRoot(ElementName = "Echo", Namespace = "urn:test")]
                public sealed class EchoRequest
                {
                    [XmlElement(ElementName = "Value", Namespace = "urn:test")]
                    public string Value { get; set; } = string.Empty;
                }

                [XmlRoot(ElementName = "EchoResponse", Namespace = "urn:test")]
                public sealed class EchoResponse
                {
                    [XmlElement(ElementName = "Result", Namespace = "urn:test")]
                    public string Result { get; set; } = string.Empty;
                }

                [XmlRoot(ElementName = "Ping", Namespace = "urn:test")]
                public sealed class PingRequest
                {
                    [XmlElement(ElementName = "Value", Namespace = "urn:test")]
                    public string Value { get; set; } = string.Empty;
                }

                [XmlRoot(ElementName = "PingResponse", Namespace = "urn:test")]
                public sealed class PingResponse
                {
                    [XmlElement(ElementName = "Result", Namespace = "urn:test")]
                    public string Result { get; set; } = string.Empty;
                }

                [XmlSerializable<EchoRequest>]
                [XmlSerializable<EchoResponse>]
                [XmlSerializable<PingRequest>]
                [XmlSerializable<PingResponse>]
                public partial class TestXmlContext : XmlSerializerContext
                {
                }
            }
            """;

        #endregion

        #region Public Methods

        [Theory]
        [InlineData("[SoapService]", 1)]
        [InlineData("[SoapService(Namespace = \"   \")]", 1)]
        [InlineData("[SoapService(Namespace = \"urn:bad space\")]", 1)]
        [InlineData("[SoapService(Name = \"123Invalid\", Namespace = \"urn:test\")]", 1)]
        [InlineData("[SoapService(Name = \"bad:name\", Namespace = \"urn:test\")]", 1)]
        public void FS0001_MissingOrInvalidServiceNamespaceOrName_ReportsExactDiagnosticAndLocation(
            string serviceAttribute,
            int expectedLineZeroBased)
        {
            string source = $$"""
                using FastSoap.Abstractions;

                {{serviceAttribute}}
                public interface ISampleService
                {
                }
                """;

            string normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal);
            GeneratorRunOutcome outcome = GeneratorTestHarness.RunGenerator([("SampleService.cs", normalized)]);

            Diagnostic diagnostic = Assert.Single(outcome.GeneratorDiagnostics);
            Assert.Equal("FS0001", diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Empty(outcome.RunResult.Results[0].GeneratedSources);

            FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
            Assert.Equal("SampleService.cs", span.Path);
            Assert.Equal(expectedLineZeroBased + 1, span.StartLinePosition.Line);
        }

        [Fact]
        public void FS0002_AmbiguousRequestBodyQName_ReportsError()
        {
            const string source = """
                using System.Threading.Tasks;
                using FastSoap.Abstractions;

                namespace TestContracts
                {
                    [SoapService(Namespace = "urn:test")]
                    [SoapWireContract(typeof(TestXmlContext))]
                    public interface IDuplicateBodyService
                    {
                        [SoapOperation(Action = "urn:test/First")]
                        [SoapWireContract(RequestType = typeof(EchoRequest), ResponseType = typeof(EchoResponse))]
                        Task<EchoResponse> FirstAsync(EchoRequest request);

                        [SoapOperation(Action = "urn:test/Second")]
                        [SoapWireContract(RequestType = typeof(EchoRequest), ResponseType = typeof(PingResponse))]
                        Task<PingResponse> SecondAsync(EchoRequest request);
                    }
                }
                """;

            GeneratorRunOutcome outcome = GeneratorTestHarness.RunGenerator(
                [("Shared.cs", SharedDtoAndContextSource), ("Service.cs", source)]);

            Diagnostic diagnostic = Assert.Single(outcome.GeneratorDiagnostics);
            Assert.Equal("FS0002", diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal("Service.cs", diagnostic.Location.GetLineSpan().Path);
            Assert.Empty(outcome.RunResult.Results[0].GeneratedSources);
        }

        [Fact]
        public void FS0002_DuplicateAction_ReportsErrorOnConflictingOperation()
        {
            const string source = """
                using System.Threading.Tasks;
                using FastSoap.Abstractions;

                namespace TestContracts
                {
                    [SoapService(Namespace = "urn:test")]
                    [SoapWireContract(typeof(TestXmlContext))]
                    public interface IDuplicateActionService
                    {
                        [SoapOperation(Action = "urn:test/Same")]
                        [SoapWireContract(RequestType = typeof(EchoRequest), ResponseType = typeof(EchoResponse))]
                        Task<EchoResponse> FirstAsync(EchoRequest request);

                        [SoapOperation(Action = "urn:test/Same")]
                        [SoapWireContract(RequestType = typeof(PingRequest), ResponseType = typeof(PingResponse))]
                        Task<PingResponse> SecondAsync(PingRequest request);
                    }
                }
                """;

            GeneratorRunOutcome outcome = GeneratorTestHarness.RunGenerator(
                [("Shared.cs", SharedDtoAndContextSource), ("Service.cs", source)]);

            Diagnostic diagnostic = Assert.Single(outcome.GeneratorDiagnostics);
            Assert.Equal("FS0002", diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal("Service.cs", diagnostic.Location.GetLineSpan().Path);
            Assert.Equal(13, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
            Assert.Empty(outcome.RunResult.Results[0].GeneratedSources);
        }

        [Fact]
        public void FS0003_MethodOverload_ReportsError()
        {
            const string source = """
                using System.Threading.Tasks;
                using FastSoap.Abstractions;

                namespace TestContracts
                {
                    [SoapService(Namespace = "urn:test")]
                    [SoapWireContract(typeof(TestXmlContext))]
                    public interface IOverloadedService
                    {
                        [SoapOperation(Name = "Op1", Action = "urn:test/Op1")]
                        [SoapWireContract(RequestType = typeof(EchoRequest), ResponseType = typeof(EchoResponse))]
                        Task<EchoResponse> ProcessAsync(EchoRequest request);

                        [SoapOperation(Name = "Op2", Action = "urn:test/Op2")]
                        [SoapWireContract(RequestType = typeof(PingRequest), ResponseType = typeof(PingResponse))]
                        Task<PingResponse> ProcessAsync(PingRequest request);
                    }
                }
                """;

            GeneratorRunOutcome outcome = GeneratorTestHarness.RunGenerator(
                [("Shared.cs", SharedDtoAndContextSource), ("Service.cs", source)]);

            ImmutableArray<Diagnostic> fs0003 = outcome.GeneratorDiagnostics
                .Where(static d => d.Id == "FS0003")
                .ToImmutableArray();

            Assert.Equal(2, fs0003.Length);
            Assert.Empty(outcome.RunResult.Results[0].GeneratedSources);
        }

        [Fact]
        public void FS0003_OpenGenericService_ReportsError()
        {
            const string source = """
                using System.Threading.Tasks;
                using FastSoap.Abstractions;

                namespace TestContracts
                {
                    [SoapService(Namespace = "urn:test")]
                    [SoapWireContract(typeof(TestXmlContext))]
                    public interface IGenericService<T>
                    {
                        [SoapOperation(Action = "urn:test/Echo")]
                        [SoapWireContract(RequestType = typeof(EchoRequest), ResponseType = typeof(EchoResponse))]
                        Task<EchoResponse> EchoAsync(EchoRequest request);
                    }
                }
                """;

            GeneratorRunOutcome outcome = GeneratorTestHarness.RunGenerator(
                [("Shared.cs", SharedDtoAndContextSource), ("Service.cs", source)]);

            Diagnostic diagnostic = Assert.Single(outcome.GeneratorDiagnostics);
            Assert.Equal("FS0003", diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal(7, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
            Assert.Empty(outcome.RunResult.Results[0].GeneratedSources);
        }

        [Theory]
        [InlineData("Task<EchoResponse> EchoAsync<T>(EchoRequest request);")]
        [InlineData("Task<EchoResponse> EchoAsync(ref EchoRequest request);")]
        [InlineData("Task<EchoResponse> EchoAsync(out EchoRequest request);")]
        [InlineData("void Echo(EchoRequest request);")]
        [InlineData("Task EchoAsync(EchoRequest request);")]
        [InlineData("EchoResponse Echo(EchoRequest request);")]
        [InlineData("Task<EchoResponse> EchoAsync();")]
        [InlineData("Task<EchoResponse> EchoAsync(EchoRequest first, EchoRequest second);")]
        public void FS0003_UnsupportedMethodSignatures_ReportExactDiagnostic(string methodDeclaration)
        {
            string source = $$"""
                using System.Threading;
                using System.Threading.Tasks;
                using FastSoap.Abstractions;

                namespace TestContracts
                {
                    [SoapService(Namespace = "urn:test")]
                    [SoapWireContract(typeof(TestXmlContext))]
                    public interface IInvalidSignatureService
                    {
                        [SoapOperation(Action = "urn:test/Echo")]
                        [SoapWireContract(RequestType = typeof(EchoRequest), ResponseType = typeof(EchoResponse))]
                        {{methodDeclaration}}
                    }
                }
                """;

            GeneratorRunOutcome outcome = GeneratorTestHarness.RunGenerator(
                [("Shared.cs", SharedDtoAndContextSource), ("Service.cs", source)]);

            Assert.Contains(outcome.GeneratorDiagnostics, static d => d.Id == "FS0003" && d.Severity == DiagnosticSeverity.Error);
            Assert.Empty(outcome.RunResult.Results[0].GeneratedSources);
        }

        [Fact]
        public void FS0004_MissingMethodWireContractAttribute_ReportsError()
        {
            const string source = """
                using System.Threading.Tasks;
                using FastSoap.Abstractions;

                namespace TestContracts
                {
                    [SoapService(Namespace = "urn:test")]
                    [SoapWireContract(typeof(TestXmlContext))]
                    public interface IMissingWireService
                    {
                        [SoapOperation(Action = "urn:test/Echo")]
                        Task<EchoResponse> EchoAsync(EchoRequest request);
                    }
                }
                """;

            GeneratorRunOutcome outcome = GeneratorTestHarness.RunGenerator(
                [("Shared.cs", SharedDtoAndContextSource), ("Service.cs", source)]);

            Diagnostic diagnostic = Assert.Single(outcome.GeneratorDiagnostics);
            Assert.Equal("FS0004", diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal(10, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
            Assert.Empty(outcome.RunResult.Results[0].GeneratedSources);
        }

        [Fact]
        public void FS0004_MissingOrMismatchedWireMembers_ReportError()
        {
            const string source = """
                using System.Threading.Tasks;
                using FastSoap.Abstractions;

                namespace TestContracts
                {
                    [SoapService(Namespace = "urn:test")]
                    [SoapWireContract(typeof(TestXmlContext))]
                    public interface IMismatchedMemberService
                    {
                        [SoapOperation(Action = "urn:test/Echo")]
                        [SoapWireContract(
                            RequestType = typeof(EchoRequest),
                            RequestMemberName = "NonExistentProperty",
                            ResponseType = typeof(EchoResponse))]
                        Task<EchoResponse> EchoAsync(EchoRequest request);
                    }
                }
                """;

            GeneratorRunOutcome outcome = GeneratorTestHarness.RunGenerator(
                [("Shared.cs", SharedDtoAndContextSource), ("Service.cs", source)]);

            Diagnostic diagnostic = Assert.Single(outcome.GeneratorDiagnostics);
            Assert.Equal("FS0004", diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Empty(outcome.RunResult.Results[0].GeneratedSources);
        }

        [Fact]
        public void FS0005_ContextMissingXmlSerializableForDto_ReportsError()
        {
            const string source = """
                using System.Threading.Tasks;
                using CloudNimble.Text.Xml.Serialization;
                using FastSoap.Abstractions;

                namespace TestContracts
                {
                    [XmlSerializable<EchoRequest>]
                    public partial class IncompleteXmlContext : XmlSerializerContext
                    {
                    }

                    [SoapService(Namespace = "urn:test")]
                    [SoapWireContract(typeof(IncompleteXmlContext))]
                    public interface IIncompleteContextService
                    {
                        [SoapOperation(Action = "urn:test/Echo")]
                        [SoapWireContract(RequestType = typeof(EchoRequest), ResponseType = typeof(EchoResponse))]
                        Task<EchoResponse> EchoAsync(EchoRequest request);
                    }
                }
                """;

            GeneratorRunOutcome outcome = GeneratorTestHarness.RunGenerator(
                [("Shared.cs", SharedDtoAndContextSource), ("Service.cs", source)]);

            Diagnostic diagnostic = Assert.Single(outcome.GeneratorDiagnostics);
            Assert.Equal("FS0005", diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Empty(outcome.RunResult.Results[0].GeneratedSources);
        }

        [Fact]
        public void FS0005_MissingContextTypeAndNonContextType_ReportError()
        {
            const string missingContextSource = """
                using System.Threading.Tasks;
                using FastSoap.Abstractions;

                namespace TestContracts
                {
                    [SoapService(Namespace = "urn:test")]
                    public interface INoContextService
                    {
                        [SoapOperation(Action = "urn:test/Echo")]
                        [SoapWireContract(RequestType = typeof(EchoRequest), ResponseType = typeof(EchoResponse))]
                        Task<EchoResponse> EchoAsync(EchoRequest request);
                    }
                }
                """;

            GeneratorRunOutcome missingOutcome = GeneratorTestHarness.RunGenerator(
                [("Shared.cs", SharedDtoAndContextSource), ("Service.cs", missingContextSource)]);

            Diagnostic missingDiag = Assert.Single(missingOutcome.GeneratorDiagnostics);
            Assert.Equal("FS0005", missingDiag.Id);

            const string wrongContextSource = """
                using System.Threading.Tasks;
                using FastSoap.Abstractions;

                namespace TestContracts
                {
                    public sealed class NotAContext { }

                    [SoapService(Namespace = "urn:test")]
                    [SoapWireContract(typeof(NotAContext))]
                    public interface IWrongContextService
                    {
                        [SoapOperation(Action = "urn:test/Echo")]
                        [SoapWireContract(RequestType = typeof(EchoRequest), ResponseType = typeof(EchoResponse))]
                        Task<EchoResponse> EchoAsync(EchoRequest request);
                    }
                }
                """;

            GeneratorRunOutcome wrongOutcome = GeneratorTestHarness.RunGenerator(
                [("Shared.cs", SharedDtoAndContextSource), ("Service.cs", wrongContextSource)]);

            Assert.Contains(wrongOutcome.GeneratorDiagnostics, static d => d.Id == "FS0005" && d.Severity == DiagnosticSeverity.Error);
        }

        [Fact]
        public void FS0006_InvalidQNameAndMissingXmlRoot_ReportError()
        {
            const string invalidQNameSource = """
                using System.Threading.Tasks;
                using CloudNimble.Text.Xml.Serialization;
                using FastSoap.Abstractions;

                namespace TestContracts
                {
                    [XmlRoot(ElementName = "123:Bad", Namespace = "urn:test")]
                    public sealed class BadQNameRequest
                    {
                        [XmlElement(ElementName = "Value", Namespace = "urn:test")]
                        public string Value { get; set; } = string.Empty;
                    }

                    [XmlSerializable<BadQNameRequest>]
                    [XmlSerializable<EchoResponse>]
                    public partial class BadQNameContext : XmlSerializerContext { }

                    [SoapService(Namespace = "urn:test")]
                    [SoapWireContract(typeof(BadQNameContext))]
                    public interface IBadQNameService
                    {
                        [SoapOperation(Action = "urn:test/Bad", RequestName = "bad:name")]
                        [SoapWireContract(RequestType = typeof(BadQNameRequest), ResponseType = typeof(EchoResponse))]
                        Task<EchoResponse> BadAsync(BadQNameRequest request);
                    }
                }
                """;

            GeneratorRunOutcome invalidQNameOutcome = GeneratorTestHarness.RunGenerator(
                [("Shared.cs", SharedDtoAndContextSource), ("Service.cs", invalidQNameSource)]);

            Assert.Contains(invalidQNameOutcome.GeneratorDiagnostics, static d => d.Id == "FS0006" && d.Severity == DiagnosticSeverity.Error);
            Assert.Empty(invalidQNameOutcome.RunResult.Results[0].GeneratedSources);

            const string missingXmlRootSource = """
                using System.Threading.Tasks;
                using CloudNimble.Text.Xml.Serialization;
                using FastSoap.Abstractions;

                namespace TestContracts
                {
                    public sealed class UnannotatedRequest
                    {
                        public string Value { get; set; } = string.Empty;
                    }

                    [XmlSerializable<UnannotatedRequest>]
                    [XmlSerializable<EchoResponse>]
                    public partial class UnannotatedContext : XmlSerializerContext { }

                    [SoapService(Namespace = "urn:test")]
                    [SoapWireContract(typeof(UnannotatedContext))]
                    public interface IMissingRootService
                    {
                        [SoapOperation(Action = "urn:test/Run")]
                        [SoapWireContract(RequestType = typeof(UnannotatedRequest), ResponseType = typeof(EchoResponse))]
                        Task<EchoResponse> RunAsync(UnannotatedRequest request);
                    }
                }
                """;

            GeneratorRunOutcome missingRootOutcome = GeneratorTestHarness.RunGenerator(
                [("Shared.cs", SharedDtoAndContextSource), ("Service.cs", missingXmlRootSource)]);

            Diagnostic missingRootDiag = Assert.Single(missingRootOutcome.GeneratorDiagnostics);
            Assert.Equal("FS0006", missingRootDiag.Id);
            Assert.Empty(missingRootOutcome.RunResult.Results[0].GeneratedSources);
        }

        [Fact]
        public void FS0007_InaccessibleServiceTypeAndMember_ReportError()
        {
            const string privateNestedServiceSource = """
                using System.Threading.Tasks;
                using FastSoap.Abstractions;

                namespace TestContracts
                {
                    public static class Container
                    {
                        [SoapService(Namespace = "urn:test")]
                        [SoapWireContract(typeof(TestXmlContext))]
                        private interface IHiddenService
                        {
                            [SoapOperation(Action = "urn:test/Echo")]
                            [SoapWireContract(RequestType = typeof(EchoRequest), ResponseType = typeof(EchoResponse))]
                            Task<EchoResponse> EchoAsync(EchoRequest request);
                        }
                    }
                }
                """;

            GeneratorRunOutcome hiddenServiceOutcome = GeneratorTestHarness.RunGenerator(
                [("Shared.cs", SharedDtoAndContextSource), ("Service.cs", privateNestedServiceSource)]);

            Diagnostic hiddenServiceDiag = Assert.Single(hiddenServiceOutcome.GeneratorDiagnostics);
            Assert.Equal("FS0007", hiddenServiceDiag.Id);
            Assert.Equal(DiagnosticSeverity.Error, hiddenServiceDiag.Severity);
            Assert.Empty(hiddenServiceOutcome.RunResult.Results[0].GeneratedSources);

            const string privateMemberSource = """
                using System.Threading.Tasks;
                using CloudNimble.Text.Xml.Serialization;
                using FastSoap.Abstractions;

                namespace TestContracts
                {
                    [XmlRoot(ElementName = "WrappedReq", Namespace = "urn:test")]
                    public sealed class PrivateGetterRequest
                    {
                        public string Inner { private get; set; } = string.Empty;
                    }

                    [XmlSerializable<PrivateGetterRequest>]
                    [XmlSerializable<EchoResponse>]
                    public partial class PrivateMemberContext : XmlSerializerContext { }

                    [SoapService(Namespace = "urn:test")]
                    [SoapWireContract(typeof(PrivateMemberContext))]
                    public interface IPrivateMemberService
                    {
                        [SoapOperation(Action = "urn:test/Echo")]
                        [SoapWireContract(
                            RequestType = typeof(PrivateGetterRequest),
                            RequestMemberName = nameof(PrivateGetterRequest.Inner),
                            ResponseType = typeof(EchoResponse))]
                        Task<EchoResponse> EchoAsync(string request);
                    }
                }
                """;

            GeneratorRunOutcome privateMemberOutcome = GeneratorTestHarness.RunGenerator(
                [("Shared.cs", SharedDtoAndContextSource), ("Service.cs", privateMemberSource)]);

            Diagnostic privateMemberDiag = Assert.Single(privateMemberOutcome.GeneratorDiagnostics);
            Assert.Equal("FS0007", privateMemberDiag.Id);
            Assert.Equal(DiagnosticSeverity.Error, privateMemberDiag.Severity);
            Assert.Empty(privateMemberOutcome.RunResult.Results[0].GeneratedSources);

            const string fileLocalServiceSource = """
                using System.Threading.Tasks;
                using FastSoap.Abstractions;

                namespace TestContracts
                {
                    [SoapService(Namespace = "urn:test")]
                    [SoapWireContract(typeof(TestXmlContext))]
                    file interface IFileLocalService
                    {
                        [SoapOperation(Action = "urn:test/Echo")]
                        [SoapWireContract(RequestType = typeof(EchoRequest), ResponseType = typeof(EchoResponse))]
                        Task<EchoResponse> EchoAsync(EchoRequest request);
                    }
                }
                """;

            GeneratorRunOutcome fileLocalOutcome = GeneratorTestHarness.RunGenerator(
                [("Shared.cs", SharedDtoAndContextSource), ("Service.cs", fileLocalServiceSource)]);

            Diagnostic fileLocalDiag = Assert.Single(fileLocalOutcome.GeneratorDiagnostics);
            Assert.Equal("FS0007", fileLocalDiag.Id);
            Assert.Equal(DiagnosticSeverity.Error, fileLocalDiag.Severity);
            Assert.Empty(fileLocalOutcome.RunResult.Results[0].GeneratedSources);
        }

        [Fact]
        public void FS0008_ConflictingRegistrationMethodNamesAcrossNamespaces_ReportsErrorAndDoesNotThrowDuplicateHint()
        {
            const string serviceA = """
                using System.Threading.Tasks;
                using FastSoap.Abstractions;
                using TestContracts;

                namespace Alpha
                {
                    [SoapService(Namespace = "urn:alpha")]
                    [SoapWireContract(typeof(TestXmlContext))]
                    public interface IEchoService
                    {
                        [SoapOperation(Action = "urn:alpha/Echo")]
                        [SoapWireContract(RequestType = typeof(EchoRequest), ResponseType = typeof(EchoResponse))]
                        Task<EchoResponse> EchoAsync(EchoRequest request);
                    }
                }
                """;

            const string serviceB = """
                using System.Threading.Tasks;
                using FastSoap.Abstractions;
                using TestContracts;

                namespace Beta
                {
                    [SoapService(Namespace = "urn:beta")]
                    [SoapWireContract(typeof(TestXmlContext))]
                    public interface IEchoService
                    {
                        [SoapOperation(Action = "urn:beta/Echo")]
                        [SoapWireContract(RequestType = typeof(EchoRequest), ResponseType = typeof(EchoResponse))]
                        Task<EchoResponse> EchoAsync(EchoRequest request);
                    }
                }
                """;

            GeneratorRunOutcome outcome = GeneratorTestHarness.RunGenerator(
                [("Shared.cs", SharedDtoAndContextSource), ("Alpha.cs", serviceA), ("Beta.cs", serviceB)]);

            ImmutableArray<Diagnostic> fs0008 = outcome.GeneratorDiagnostics
                .Where(static d => d.Id == "FS0008")
                .ToImmutableArray();

            Assert.Equal(2, fs0008.Length);
            Assert.All(fs0008, static d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
            Assert.Empty(outcome.RunResult.Results[0].GeneratedSources);
        }

        [Fact]
        public void FS0008_ExistingAuthoredRegistrationMethod_ReportsConflict()
        {
            const string source = """
                using System.Threading.Tasks;
                using FastSoap.Abstractions;
                using Microsoft.AspNetCore.Builder;
                using Microsoft.AspNetCore.Routing;

                namespace Microsoft.AspNetCore.Builder
                {
                    public static class ExistingExtensions
                    {
                        public static IEndpointConventionBuilder MapSampleService(
                            this IEndpointRouteBuilder endpoints,
                            string pattern)
                        {
                            throw new System.NotSupportedException();
                        }
                    }
                }

                namespace TestContracts
                {
                    [SoapService(Name = "SampleService", Namespace = "urn:test")]
                    [SoapWireContract(typeof(TestXmlContext))]
                    public interface ISampleService
                    {
                        [SoapOperation(Action = "urn:test/Echo")]
                        [SoapWireContract(RequestType = typeof(EchoRequest), ResponseType = typeof(EchoResponse))]
                        Task<EchoResponse> EchoAsync(EchoRequest request);
                    }
                }
                """;

            GeneratorRunOutcome outcome = GeneratorTestHarness.RunGenerator(
                [("Shared.cs", SharedDtoAndContextSource), ("Service.cs", source)]);

            Diagnostic diagnostic = Assert.Single(outcome.GeneratorDiagnostics);
            Assert.Equal("FS0008", diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Empty(outcome.RunResult.Results[0].GeneratedSources);
        }

        #endregion

    }

}
