using System;
using System.Security.Claims;
using System.Threading.Tasks;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

using FastSoap.Abstractions;
using FastSoap.Benchmarks.Contracts;
using FastSoap.Benchmarks.Services;
using FastSoap.Core.Dispatching;
using FastSoap.Core.Protocol;

namespace FastSoap.Benchmarks.Benchmarks
{

    /// <summary>
    /// End-to-end in-process <see cref="SoapMessageProcessor"/> dispatch microbenchmarks covering
    /// Echo and nested Transfer (urn:payments) under manual static and generated/DI-resolved registrations.
    /// </summary>
    [MemoryDiagnoser]
    [ShortRunJob]
    public class DispatchBenchmarks
    {

        #region Fields

        private SoapRequestMetadata _echoMetadata;
        private ReadOnlyMemory<byte> _echoRequestMemory;
        private SoapMessageProcessor _generatedProcessor = null!;
        private SoapMessageProcessor _manualProcessor = null!;
        private IServiceProvider _services = null!;
        private SoapRequestMetadata _transferMetadata;
        private ReadOnlyMemory<byte> _transferRequestMemory;
        private ClaimsPrincipal _user = null!;

        #endregion

        #region Public Methods

        /// <summary>
        /// Initializes shared processors, descriptors, service provider, and UTF-8 request payloads once per benchmark process.
        /// </summary>
        [GlobalSetup]
        public void GlobalSetup()
        {
            _echoRequestMemory = BenchmarkPayloads.EchoSoap11RequestUtf8;
            _transferRequestMemory = BenchmarkPayloads.TransferSoap11RequestUtf8;

            _echoMetadata = new SoapRequestMetadata(
                SoapConstants.Soap11ResponseContentType,
                BenchmarkPayloads.QuotedEchoAction);

            _transferMetadata = new SoapRequestMetadata(
                SoapConstants.Soap11ResponseContentType,
                BenchmarkPayloads.QuotedTransferAction);

            _services = new BenchmarkServiceProvider(
                new BenchmarkEchoService(),
                new BenchmarkPaymentService());

            _user = new ClaimsPrincipal(new ClaimsIdentity());

            SoapEndpointOptions options = new SoapEndpointOptions
            {
                EnabledVersions = SoapVersionFlags.Soap11,
                ClearPooledBuffers = true
            };

            SoapOperationTableBuilder manualBuilder = new SoapOperationTableBuilder();
            manualBuilder.MapOperation(
                BenchmarkPayloads.CreateManualEchoDescriptor(),
                BenchmarkPayloads.SharedXmlContext.EchoStringRequestWire,
                BenchmarkPayloads.SharedXmlContext.EchoStringResponseWire,
                static (wire, _, ct) =>
                {
                    ct.ThrowIfCancellationRequested();

                    return ValueTask.FromResult(new EchoStringResponseWire
                    {
                        EchoStringResult = wire.Echo
                    });
                });

            manualBuilder.MapOperation(
                BenchmarkPayloads.CreateManualTransferDescriptor(),
                BenchmarkPayloads.SharedXmlContext.TransferRequestWire,
                BenchmarkPayloads.SharedXmlContext.TransferResponseWire,
                static (wire, _, ct) =>
                {
                    ct.ThrowIfCancellationRequested();

                    return ValueTask.FromResult(new TransferResponseWire
                    {
                        TransferResult = BenchmarkPayloads.ExecuteTransfer(wire.Request)
                    });
                });

            _manualProcessor = new SoapMessageProcessor(options, manualBuilder.Build(options));

            SoapOperationTableBuilder generatedBuilder = new SoapOperationTableBuilder();
            generatedBuilder.MapOperation(
                BenchmarkEchoServiceSoapContract.EchoString,
                BenchmarkXmlContext.Default.EchoStringRequestWire,
                BenchmarkXmlContext.Default.EchoStringResponseWire,
                static async (wire, context, ct) =>
                {
                    IBenchmarkEchoService service = (IBenchmarkEchoService)context.Services.GetService(typeof(IBenchmarkEchoService))!;

                    return await service.EchoStringAsync(wire, ct).ConfigureAwait(false);
                });

            generatedBuilder.MapOperation(
                BenchmarkPaymentServiceSoapContract.Transfer,
                BenchmarkXmlContext.Default.TransferRequestWire,
                BenchmarkXmlContext.Default.TransferResponseWire,
                static async (wire, context, ct) =>
                {
                    IBenchmarkPaymentService service = (IBenchmarkPaymentService)context.Services.GetService(typeof(IBenchmarkPaymentService))!;
                    TransferResult result = await service.TransferAsync(wire.Request, ct).ConfigureAwait(false);

                    return new TransferResponseWire
                    {
                        TransferResult = result
                    };
                });

            _generatedProcessor = new SoapMessageProcessor(options, generatedBuilder.Build(options));
        }

        /// <summary>
        /// Benchmarks end-to-end <c>SoapMessageProcessor.ProcessAsync</c> on <c>EchoString</c> with generated contract descriptors and service resolution.
        /// </summary>
        /// <returns>The staged response length in bytes.</returns>
        [Benchmark]
        public async ValueTask<int> ProcessAsync_Echo_Generated()
        {
            using SoapProcessingResult result = await _generatedProcessor.ProcessAsync(
                _echoMetadata,
                _echoRequestMemory,
                _services,
                _user).ConfigureAwait(false);

            return result.Body.Length;
        }

        /// <summary>
        /// Benchmarks end-to-end <c>SoapMessageProcessor.ProcessAsync</c> on <c>EchoString</c> with manual registration and static handler.
        /// </summary>
        /// <returns>The staged response length in bytes.</returns>
        [Benchmark]
        public async ValueTask<int> ProcessAsync_Echo_Manual()
        {
            using SoapProcessingResult result = await _manualProcessor.ProcessAsync(
                _echoMetadata,
                _echoRequestMemory,
                _services,
                _user).ConfigureAwait(false);

            return result.Body.Length;
        }

        /// <summary>
        /// Benchmarks end-to-end <c>SoapMessageProcessor.ProcessAsync</c> on nested <c>Transfer</c> with generated contract descriptors and service resolution.
        /// </summary>
        /// <returns>The staged response length in bytes.</returns>
        [Benchmark]
        public async ValueTask<int> ProcessAsync_Transfer_Generated()
        {
            using SoapProcessingResult result = await _generatedProcessor.ProcessAsync(
                _transferMetadata,
                _transferRequestMemory,
                _services,
                _user).ConfigureAwait(false);

            return result.Body.Length;
        }

        /// <summary>
        /// Benchmarks end-to-end <c>SoapMessageProcessor.ProcessAsync</c> on nested <c>Transfer</c> with manual registration and static handler.
        /// </summary>
        /// <returns>The staged response length in bytes.</returns>
        [Benchmark]
        public async ValueTask<int> ProcessAsync_Transfer_Manual()
        {
            using SoapProcessingResult result = await _manualProcessor.ProcessAsync(
                _transferMetadata,
                _transferRequestMemory,
                _services,
                _user).ConfigureAwait(false);

            return result.Body.Length;
        }

        #endregion

    }

}
