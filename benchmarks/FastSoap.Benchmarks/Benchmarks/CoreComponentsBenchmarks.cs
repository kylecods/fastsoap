using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Security.Claims;
using System.Threading.Tasks;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

using FastSoap.Abstractions;
using FastSoap.Benchmarks.Contracts;
using FastSoap.Benchmarks.Services;
using FastSoap.Core.Buffers;
using FastSoap.Core.Dispatching;
using FastSoap.Core.Protocol;
using FastSoap.Core.Serialization;

namespace FastSoap.Benchmarks.Benchmarks
{

    /// <summary>
    /// Component-level microbenchmarks for <see cref="SoapEnvelopeReader"/>, <see cref="SoapOperationTable"/>,
    /// <see cref="SoapRequestBuffer"/>, <see cref="BoundedPooledBufferWriter"/>, <see cref="SoapResponseWriter"/>,
    /// and <see cref="SoapFaultWriter"/>.
    /// </summary>
    [MemoryDiagnoser]
    [ShortRunJob]
    public class CoreComponentsBenchmarks
    {

        #region Fields

        private SoapOperationTable _actionlessTable = null!;
        private byte[] _chunk256 = null!;
        private byte[] _chunk2KiB = null!;
        private byte[] _echoRequestBytes = null!;
        private EchoStringResponseWire _echoResponse = null!;
        private SoapEndpointOptions _options = null!;
        private Pipe _pipe = null!;
        private ArrayBufferWriter<byte> _reusableBuffer = null!;
        private IServiceProvider _services = null!;
        private SoapFault _soap11Fault = null!;
        private SoapFault _soap12Fault = null!;
        private SoapOperationTable _strictTable = null!;
        private SoapQName _transferQName;
        private byte[] _transferRequestBytes = null!;
        private TransferResponseWire _transferResponse = null!;
        private ClaimsPrincipal _user = null!;

        #endregion

        #region Public Methods

        /// <summary>
        /// Benchmarks HTTP <c>Content-Type</c> and <c>SOAPAction</c> header parsing for SOAP 1.1.
        /// </summary>
        /// <returns>The parsed action length.</returns>
        [Benchmark]
        public int ContentTypeAndActionParser_Soap11()
        {
            SoapContentTypeInfo contentType = SoapContentTypeParser.Parse(
                SoapConstants.Soap11ResponseContentType,
                _options.EnabledVersions,
                _options.MaxActionLength);

            string? action = SoapActionParser.ParseSoap11Action(
                BenchmarkPayloads.QuotedTransferAction,
                hasMultipleHeaders: false,
                allowMissingSoap11Action: false,
                maxActionLength: _options.MaxActionLength);

            return (int)contentType.Version + (action?.Length ?? 0);
        }

        /// <summary>
        /// Benchmarks synchronous <see cref="SoapEnvelopeReader.ParseAndPrepare"/> on the SOAP 1.1 Echo envelope.
        /// </summary>
        /// <returns>The resolved operation action length.</returns>
        [Benchmark]
        public int EnvelopeReader_ParseAndPrepare_Echo()
        {
            PreparedSoapCall call = SoapEnvelopeReader.ParseAndPrepare(
                _echoRequestBytes,
                SoapVersion.Soap11,
                BenchmarkConstants.EchoAction,
                _strictTable,
                _options,
                _services,
                _user);

            return call.Descriptor.Action.Length;
        }

        /// <summary>
        /// Benchmarks synchronous <see cref="SoapEnvelopeReader.ParseAndPrepare"/> on the nested <c>urn:payments</c> Transfer envelope.
        /// </summary>
        /// <returns>The resolved operation action length.</returns>
        [Benchmark]
        public int EnvelopeReader_ParseAndPrepare_Transfer()
        {
            PreparedSoapCall call = SoapEnvelopeReader.ParseAndPrepare(
                _transferRequestBytes,
                SoapVersion.Soap11,
                BenchmarkConstants.TransferAction,
                _strictTable,
                _options,
                _services,
                _user);

            return call.Descriptor.Action.Length;
        }

        /// <summary>
        /// Benchmarks <see cref="SoapFaultWriter.WriteFault"/> for a standard SOAP 1.1 Client fault into a pooled buffer writer.
        /// </summary>
        /// <returns>The written byte count.</returns>
        [Benchmark]
        public int FaultWriter_Soap11_Pooled()
        {
            using BoundedPooledBufferWriter writer = new BoundedPooledBufferWriter(512, 65536, clearOnReturn: true);
            SoapFaultWriter.WriteFault(_soap11Fault, writer);

            return writer.WrittenCount;
        }

        /// <summary>
        /// Benchmarks <see cref="SoapFaultWriter.WriteFault"/> for a standard SOAP 1.2 Sender fault into a reusable buffer writer.
        /// </summary>
        /// <returns>The written byte count.</returns>
        [Benchmark]
        public int FaultWriter_Soap12_ReusableBuffer()
        {
            _reusableBuffer.ResetWrittenCount();
            SoapFaultWriter.WriteFault(_soap12Fault, _reusableBuffer);

            return _reusableBuffer.WrittenCount;
        }

        /// <summary>
        /// Initializes shared fixtures, routing tables, buffers, and DTOs once per benchmark process.
        /// </summary>
        [GlobalSetup]
        public void GlobalSetup()
        {
            _echoRequestBytes = BenchmarkPayloads.EchoSoap11RequestUtf8;
            _transferRequestBytes = BenchmarkPayloads.TransferSoap11RequestUtf8;
            _transferQName = new SoapQName(BenchmarkConstants.PaymentsNamespace, "Transfer");

            _chunk256 = new byte[256];
            _chunk256.AsSpan().Fill((byte)'x');

            _chunk2KiB = new byte[2048];
            _chunk2KiB.AsSpan().Fill((byte)'y');

            _echoResponse = new EchoStringResponseWire
            {
                EchoStringResult = "hello-fastsoap"
            };

            _transferResponse = new TransferResponseWire
            {
                TransferResult = new TransferResult
                {
                    TransactionId = "TX-001",
                    Successful = true,
                    ProcessedAtUtc = BenchmarkPayloads.FixedSettlementUtc,
                    Status = PaymentStatus.Completed
                }
            };

            _soap11Fault = SoapFault.CreateStandard(
                SoapVersion.Soap11,
                SoapFaultKind.Sender,
                "SOAP request payload failed schema or type validation.");

            _soap12Fault = SoapFault.CreateStandard(
                SoapVersion.Soap12,
                SoapFaultKind.Sender,
                "SOAP request payload failed schema or type validation.");

            _services = new BenchmarkServiceProvider(
                new BenchmarkEchoService(),
                new BenchmarkPaymentService());
            _user = new ClaimsPrincipal(new ClaimsIdentity());

            _options = new SoapEndpointOptions
            {
                EnabledVersions = SoapVersionFlags.Soap11,
                AllowMissingSoap11Action = false
            };

            SoapEndpointOptions actionlessOptions = new SoapEndpointOptions
            {
                EnabledVersions = SoapVersionFlags.Both,
                AllowMissingSoap11Action = true
            };

            _strictTable = BuildTable(_options);
            _actionlessTable = BuildTable(actionlessOptions);

            _pipe = new Pipe();
            _pipe.Writer.Complete();
            _pipe.Reader.Complete();
            _reusableBuffer = new ArrayBufferWriter<byte>(4096);
        }

        /// <summary>
        /// Benchmarks exact ordinal <see cref="SoapOperationTable.TryGetByAction"/> routing lookup.
        /// </summary>
        /// <returns>Whether the operation was resolved.</returns>
        [Benchmark]
        public bool OperationTable_TryGetByAction_Transfer()
        {
            return _strictTable.TryGetByAction(BenchmarkConstants.TransferAction, out _);
        }

        /// <summary>
        /// Benchmarks actionless <see cref="SoapOperationTable.TryGetByRequestElement"/> QName routing lookup.
        /// </summary>
        /// <returns>Whether the operation was resolved.</returns>
        [Benchmark]
        public bool OperationTable_TryGetByRequestElement_Transfer()
        {
            return _actionlessTable.TryGetByRequestElement(_transferQName, out _);
        }

        /// <summary>
        /// Benchmarks <see cref="SoapRequestBuffer.CopyFromPipeAsync"/> plus clear/return on the Echo payload.
        /// </summary>
        /// <returns>The copied logical byte length.</returns>
        [Benchmark]
        public async ValueTask<int> RequestBuffer_CopyFromPipe_Echo()
        {
            _pipe.Reset();
            BuffersExtensions.Write(_pipe.Writer, _echoRequestBytes);
            await _pipe.Writer.CompleteAsync().ConfigureAwait(false);

            using SoapRequestBuffer buffer = await SoapRequestBuffer.CopyFromPipeAsync(
                _pipe.Reader,
                _options.MaxRequestBytes,
                clearOnReturn: true).ConfigureAwait(false);

            int length = buffer.Length;
            await _pipe.Reader.CompleteAsync().ConfigureAwait(false);

            return length;
        }

        /// <summary>
        /// Benchmarks <see cref="BoundedPooledBufferWriter"/> rent, 256-byte write, clear, and pool return.
        /// </summary>
        /// <returns>The written byte count.</returns>
        [Benchmark]
        public int ResponseBuffer_RentWriteDispose_256B()
        {
            using BoundedPooledBufferWriter writer = new BoundedPooledBufferWriter(4096, 65536, clearOnReturn: true);
            BuffersExtensions.Write(writer, _chunk256);

            return writer.WrittenCount;
        }

        /// <summary>
        /// Benchmarks <see cref="BoundedPooledBufferWriter"/> rent, 2 KiB write, clear, and pool return.
        /// </summary>
        /// <returns>The written byte count.</returns>
        [Benchmark]
        public int ResponseBuffer_RentWriteDispose_2KiB()
        {
            using BoundedPooledBufferWriter writer = new BoundedPooledBufferWriter(4096, 65536, clearOnReturn: true);
            BuffersExtensions.Write(writer, _chunk2KiB);

            return writer.WrittenCount;
        }

        /// <summary>
        /// Benchmarks <see cref="SoapResponseWriter.WriteResponse{TResponse}"/> for Echo into a pooled buffer writer.
        /// </summary>
        /// <returns>The written byte count.</returns>
        [Benchmark]
        public int ResponseWriter_Echo_Soap11_Pooled()
        {
            using BoundedPooledBufferWriter writer = new BoundedPooledBufferWriter(4096, 65536, clearOnReturn: true);
            SoapResponseWriter.WriteResponse(
                SoapVersion.Soap11,
                writer,
                _echoResponse,
                BenchmarkXmlContext.Default.EchoStringResponseWire);

            return writer.WrittenCount;
        }

        /// <summary>
        /// Benchmarks <see cref="SoapResponseWriter.WriteResponse{TResponse}"/> for Transfer into a pooled buffer writer.
        /// </summary>
        /// <returns>The written byte count.</returns>
        [Benchmark]
        public int ResponseWriter_Transfer_Soap11_Pooled()
        {
            using BoundedPooledBufferWriter writer = new BoundedPooledBufferWriter(4096, 65536, clearOnReturn: true);
            SoapResponseWriter.WriteResponse(
                SoapVersion.Soap11,
                writer,
                _transferResponse,
                BenchmarkXmlContext.Default.TransferResponseWire);

            return writer.WrittenCount;
        }

        #endregion

        #region Private Methods

        private static SoapOperationTable BuildTable(SoapEndpointOptions options)
        {
            SoapOperationTableBuilder builder = new SoapOperationTableBuilder();
            builder.MapOperation(
                BenchmarkEchoServiceSoapContract.EchoString,
                BenchmarkXmlContext.Default.EchoStringRequestWire,
                BenchmarkXmlContext.Default.EchoStringResponseWire,
                static (wire, _, _) => ValueTask.FromResult(new EchoStringResponseWire { EchoStringResult = wire.Echo }));

            builder.MapOperation(
                BenchmarkPaymentServiceSoapContract.Transfer,
                BenchmarkXmlContext.Default.TransferRequestWire,
                BenchmarkXmlContext.Default.TransferResponseWire,
                static (wire, _, _) => ValueTask.FromResult(new TransferResponseWire
                {
                    TransferResult = BenchmarkPayloads.ExecuteTransfer(wire.Request)
                }));

            return builder.Build(options);
        }

        #endregion

    }

}
