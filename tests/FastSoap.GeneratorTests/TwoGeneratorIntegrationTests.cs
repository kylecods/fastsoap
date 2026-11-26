using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FastSoap.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FastSoap.GeneratorTests
{

    /// <summary>
    /// End-to-end two-generator compilation and execution tests verifying that
    /// <see cref="FastSoap.Generators.FastSoapContractGenerator"/> and <c>XmlEssentials 1.0.0</c>'s
    /// <c>SerializerGenerator</c> compile and execute cleanly together in the same Roslyn compilation.
    /// </summary>
    public sealed class TwoGeneratorIntegrationTests
    {

        #region Fields

        private const string ExecutableAspNetCoreStubSource = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using CloudNimble.Text.Xml;
            using CloudNimble.Text.Xml.Serialization;
            using FastSoap.Abstractions;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            namespace FastSoap.AspNetCore
            {

                public sealed class SoapEndpointBuilder
                {
                    private readonly string _pattern;
                    private readonly Action<string, SoapOperationDescriptor, Func<byte[], SoapCallContext, CancellationToken, ValueTask<byte[]>>>? _recorder;

                    public SoapEndpointBuilder(
                        string pattern,
                        Action<string, SoapOperationDescriptor, Func<byte[], SoapCallContext, CancellationToken, ValueTask<byte[]>>>? recorder)
                    {
                        _pattern = pattern;
                        _recorder = recorder;
                    }

                    public SoapEndpointBuilder MapOperation<TRequest, TResponse>(
                        SoapOperationDescriptor descriptor,
                        XmlTypeInfo<TRequest> requestType,
                        XmlTypeInfo<TResponse> responseType,
                        SoapOperationHandler<TRequest, TResponse> handler)
                    {
                        ArgumentNullException.ThrowIfNull(descriptor);
                        ArgumentNullException.ThrowIfNull(requestType);
                        ArgumentNullException.ThrowIfNull(responseType);
                        ArgumentNullException.ThrowIfNull(handler);

                        Func<byte[], SoapCallContext, CancellationToken, ValueTask<byte[]>> executor = async (utf8Bytes, context, ct) =>
                        {
                            TRequest? request = XmlSerializer.Deserialize(utf8Bytes.AsSpan(), requestType);
                            if (request is null)
                            {
                                throw new InvalidOperationException("Deserialized request was null.");
                            }

                            TResponse response = await handler(request, context, ct).ConfigureAwait(false);
                            return XmlSerializer.SerializeToUtf8Bytes(response, responseType);
                        };

                        _recorder?.Invoke(_pattern, descriptor, executor);
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

                        Action<string, SoapOperationDescriptor, Func<byte[], SoapCallContext, CancellationToken, ValueTask<byte[]>>>? recorder =
                            endpoints.ServiceProvider.GetService(
                                typeof(Action<string, SoapOperationDescriptor, Func<byte[], SoapCallContext, CancellationToken, ValueTask<byte[]>>>))
                            as Action<string, SoapOperationDescriptor, Func<byte[], SoapCallContext, CancellationToken, ValueTask<byte[]>>>;

                        SoapEndpointBuilder builder = new SoapEndpointBuilder(pattern, recorder);
                        configure(builder);
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

        private const string TwoServiceAppSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using CloudNimble.Text.Xml.Serialization;
            using FastSoap.Abstractions;

            namespace SampleApp
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

                public sealed class TransferRequest
                {
                    [XmlElement(ElementName = "AccountNumber", Namespace = "urn:payments", Order = 0)]
                    public required string AccountNumber { get; init; }

                    [XmlElement(ElementName = "Amount", Namespace = "urn:payments", Order = 1)]
                    public decimal Amount { get; init; }
                }

                public sealed class TransferResult
                {
                    [XmlElement(ElementName = "TransactionId", Namespace = "urn:payments", Order = 0)]
                    public required string TransactionId { get; init; }

                    [XmlElement(ElementName = "Successful", Namespace = "urn:payments", Order = 1)]
                    public bool Successful { get; init; }
                }

                [XmlRoot(ElementName = "Transfer", Namespace = "urn:payments")]
                public sealed class TransferRequestWire
                {
                    [XmlElement(ElementName = "request", Namespace = "urn:payments")]
                    public required TransferRequest Request { get; init; }
                }

                [XmlRoot(ElementName = "TransferResponse", Namespace = "urn:payments")]
                public sealed class TransferResponseWire
                {
                    [XmlElement(ElementName = "TransferResult", Namespace = "urn:payments")]
                    public required TransferResult Result { get; init; }
                }

                [XmlSerializable<EchoRequest>]
                [XmlSerializable<EchoResponse>]
                [XmlSerializable<TransferRequestWire>]
                [XmlSerializable<TransferResponseWire>]
                public partial class SampleAppXmlContext : XmlSerializerContext
                {
                }

                [SoapService(Namespace = "urn:echo")]
                [SoapWireContract(typeof(SampleAppXmlContext))]
                public interface IEchoService
                {
                    [SoapOperation(Action = "urn:echo/IEchoService/Echo")]
                    [SoapWireContract(RequestType = typeof(EchoRequest), ResponseType = typeof(EchoResponse))]
                    Task<EchoResponse> EchoAsync(EchoRequest request, CancellationToken cancellationToken);
                }

                [SoapService(Namespace = "urn:payments")]
                [SoapWireContract(typeof(SampleAppXmlContext))]
                public interface IPaymentService
                {
                    [SoapOperation(
                        Action = "urn:payments/IPaymentService/Transfer",
                        ReplyAction = "urn:payments/IPaymentService/TransferResponse")]
                    [SoapWireContract(
                        RequestType = typeof(TransferRequestWire),
                        RequestMemberName = nameof(TransferRequestWire.Request),
                        ResponseType = typeof(TransferResponseWire),
                        ResponseMemberName = nameof(TransferResponseWire.Result))]
                    Task<TransferResult> TransferAsync(TransferRequest request, CancellationToken cancellationToken);
                }

                public sealed class EchoServiceImpl : IEchoService
                {
                    public Task<EchoResponse> EchoAsync(EchoRequest request, CancellationToken cancellationToken)
                    {
                        return Task.FromResult(new EchoResponse { EchoResult = $"echo:{request.Message}" });
                    }
                }

                public sealed class PaymentServiceImpl : IPaymentService
                {
                    public Task<TransferResult> TransferAsync(TransferRequest request, CancellationToken cancellationToken)
                    {
                        return Task.FromResult(new TransferResult
                        {
                            TransactionId = $"TX-{request.AccountNumber}",
                            Successful = request.Amount > 0m
                        });
                    }
                }

            }
            """;

        #endregion

        #region Public Methods

        [Fact]
        public async Task TwoGeneratorsTogether_EmitCompilableAssemblyAndExecuteXmlRoundTrip()
        {
            GeneratorRunOutcome outcome = GeneratorTestHarness.RunGenerator(
                [("SampleApp.cs", TwoServiceAppSource), ("AspNetCoreStub.cs", ExecutableAspNetCoreStubSource)],
                includeXmlEssentialsGenerator: true,
                includeAspNetCoreStub: false);

            Assert.Empty(outcome.GeneratorDiagnostics);
            Assert.Empty(outcome.CompilationDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning));

            Assert.Equal(2, outcome.RunResult.Results.Length);
            Assert.Equal(4, outcome.RunResult.Results[0].GeneratedSources.Length);
            Assert.NotEmpty(outcome.RunResult.Results[1].GeneratedSources);

            using MemoryStream peStream = new MemoryStream();
            EmitResult emitResult = outcome.OutputCompilation.Emit(peStream, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(emitResult.Success, string.Join("\n", emitResult.Diagnostics));

            peStream.Position = 0;
            AssemblyLoadContext loadContext = new AssemblyLoadContext("TwoGenTest", isCollectible: true);
            try
            {
                Assembly compiledAssembly = loadContext.LoadFromStream(peStream);

                Type echoInterface = compiledAssembly.GetType("SampleApp.IEchoService", throwOnError: true)!;
                Type echoImpl = compiledAssembly.GetType("SampleApp.EchoServiceImpl", throwOnError: true)!;
                Type paymentInterface = compiledAssembly.GetType("SampleApp.IPaymentService", throwOnError: true)!;
                Type paymentImpl = compiledAssembly.GetType("SampleApp.PaymentServiceImpl", throwOnError: true)!;

                ServiceCollection services = new ServiceCollection();
                services.AddScoped(echoInterface, echoImpl);
                services.AddScoped(paymentInterface, paymentImpl);
                using ServiceProvider serviceProvider = services.BuildServiceProvider();

                List<CapturedRegistration> captured = new List<CapturedRegistration>();
                Action<string, SoapOperationDescriptor, Func<byte[], SoapCallContext, CancellationToken, ValueTask<byte[]>>> recorder =
                    (pattern, descriptor, executor) =>
                    {
                        captured.Add(new CapturedRegistration
                        {
                            Pattern = pattern,
                            Descriptor = descriptor,
                            Executor = executor
                        });
                    };

                FakeEndpointRouteBuilder routeBuilder = new FakeEndpointRouteBuilder(
                    new CompositeServiceProvider(serviceProvider, recorder));

                Type echoExtensions = compiledAssembly.GetType("Microsoft.AspNetCore.Builder.EchoServiceSoapEndpointRouteBuilderExtensions", throwOnError: true)!;
                Func<IEndpointRouteBuilder, string, IEndpointConventionBuilder> mapEcho =
                    echoExtensions.GetMethod("MapEchoService", BindingFlags.Public | BindingFlags.Static, [typeof(IEndpointRouteBuilder), typeof(string)])!
                        .CreateDelegate<Func<IEndpointRouteBuilder, string, IEndpointConventionBuilder>>();
                mapEcho(routeBuilder, "/Echo.svc");

                Type paymentExtensions = compiledAssembly.GetType("Microsoft.AspNetCore.Builder.PaymentServiceSoapEndpointRouteBuilderExtensions", throwOnError: true)!;
                Func<IEndpointRouteBuilder, string, IEndpointConventionBuilder> mapPayment =
                    paymentExtensions.GetMethod("MapPaymentService", BindingFlags.Public | BindingFlags.Static, [typeof(IEndpointRouteBuilder), typeof(string)])!
                        .CreateDelegate<Func<IEndpointRouteBuilder, string, IEndpointConventionBuilder>>();
                mapPayment(routeBuilder, "/PaymentService.svc");

                Assert.Equal(2, captured.Count);

                CapturedRegistration echoReg = captured[0];
                Assert.Equal("/Echo.svc", echoReg.Pattern);
                Assert.Equal("Echo", echoReg.Descriptor.Name);
                Assert.Equal("urn:echo/IEchoService/Echo", echoReg.Descriptor.Action);
                Assert.Equal(new SoapQName("urn:echo", "Echo"), echoReg.Descriptor.RequestElement);
                Assert.Equal(new SoapQName("urn:echo", "EchoResponse"), echoReg.Descriptor.ResponseElement);

                byte[] echoRequestBytes = "<Echo xmlns=\"urn:echo\"><Message>hello-aot</Message></Echo>"u8.ToArray();
                SoapCallContext echoContext = new SoapCallContext
                {
                    Action = echoReg.Descriptor.Action,
                    OperationDescriptor = echoReg.Descriptor,
                    Services = serviceProvider,
                    User = new ClaimsPrincipal(new ClaimsIdentity()),
                    Version = SoapVersion.Soap11
                };

                byte[] echoResponseBytes = await echoReg.Executor(echoRequestBytes, echoContext, TestContext.Current.CancellationToken);
                Assert.Equal(
                    "<EchoResponse xmlns=\"urn:echo\"><EchoResult>echo:hello-aot</EchoResult></EchoResponse>",
                    Encoding.UTF8.GetString(echoResponseBytes));

                byte[] echoDuplicateBytes = "<Echo xmlns=\"urn:echo\"><Message>one</Message><Message>two</Message></Echo>"u8.ToArray();
                await Assert.ThrowsAsync<CloudNimble.Text.Xml.XmlException>(async () =>
                    await echoReg.Executor(echoDuplicateBytes, echoContext, TestContext.Current.CancellationToken));

                byte[] echoUnmappedBytes = "<Echo xmlns=\"urn:echo\"><Message>one</Message><Extra>two</Extra></Echo>"u8.ToArray();
                await Assert.ThrowsAsync<CloudNimble.Text.Xml.XmlException>(async () =>
                    await echoReg.Executor(echoUnmappedBytes, echoContext, TestContext.Current.CancellationToken));

                CapturedRegistration transferReg = captured[1];
                Assert.Equal("/PaymentService.svc", transferReg.Pattern);
                Assert.Equal("Transfer", transferReg.Descriptor.Name);
                Assert.Equal("urn:payments/IPaymentService/Transfer", transferReg.Descriptor.Action);
                Assert.Equal("urn:payments/IPaymentService/TransferResponse", transferReg.Descriptor.ReplyAction);
                Assert.Equal(new SoapQName("urn:payments", "Transfer"), transferReg.Descriptor.RequestElement);
                Assert.Equal(new SoapQName("urn:payments", "TransferResponse"), transferReg.Descriptor.ResponseElement);

                byte[] transferRequestBytes = "<Transfer xmlns=\"urn:payments\"><request><AccountNumber>123456</AccountNumber><Amount>42.50</Amount></request></Transfer>"u8.ToArray();
                SoapCallContext transferContext = new SoapCallContext
                {
                    Action = transferReg.Descriptor.Action,
                    OperationDescriptor = transferReg.Descriptor,
                    Services = serviceProvider,
                    User = new ClaimsPrincipal(new ClaimsIdentity()),
                    Version = SoapVersion.Soap11
                };

                byte[] transferResponseBytes = await transferReg.Executor(transferRequestBytes, transferContext, TestContext.Current.CancellationToken);
                Assert.Equal(
                    "<TransferResponse xmlns=\"urn:payments\"><TransferResult><TransactionId>TX-123456</TransactionId><Successful>true</Successful></TransferResult></TransferResponse>",
                    Encoding.UTF8.GetString(transferResponseBytes));
            }
            finally
            {
                loadContext.Unload();
            }
        }

        #endregion

        private sealed class CapturedRegistration
        {

            #region Properties

            public required SoapOperationDescriptor Descriptor { get; init; }

            public required Func<byte[], SoapCallContext, CancellationToken, ValueTask<byte[]>> Executor { get; init; }

            public required string Pattern { get; init; }

            #endregion

        }

        private sealed class CompositeServiceProvider : IServiceProvider
        {

            #region Fields

            private readonly IServiceProvider _inner;
            private readonly Action<string, SoapOperationDescriptor, Func<byte[], SoapCallContext, CancellationToken, ValueTask<byte[]>>> _recorder;

            #endregion

            #region Constructors

            public CompositeServiceProvider(
                IServiceProvider inner,
                Action<string, SoapOperationDescriptor, Func<byte[], SoapCallContext, CancellationToken, ValueTask<byte[]>>> recorder)
            {
                _inner = inner;
                _recorder = recorder;
            }

            #endregion

            #region Public Methods

            public object? GetService(Type serviceType)
            {
                if (serviceType == typeof(Action<string, SoapOperationDescriptor, Func<byte[], SoapCallContext, CancellationToken, ValueTask<byte[]>>>))
                {
                    return _recorder;
                }

                return _inner.GetService(serviceType);
            }

            #endregion

        }

        private sealed class FakeEndpointRouteBuilder : IEndpointRouteBuilder
        {

            #region Properties

            public ICollection<EndpointDataSource> DataSources { get; } = [];

            public IServiceProvider ServiceProvider { get; }

            #endregion

            #region Constructors

            public FakeEndpointRouteBuilder(IServiceProvider serviceProvider)
            {
                ServiceProvider = serviceProvider;
            }

            #endregion

            #region Public Methods

            public IApplicationBuilder CreateApplicationBuilder()
            {
                throw new NotSupportedException();
            }

            #endregion

        }

    }

}
