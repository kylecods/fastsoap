using System;
using System.Threading;
using System.Threading.Tasks;

using CloudNimble.Text.Xml;
using CloudNimble.Text.Xml.Serialization;

using CoreWCF;

using FastSoap.Abstractions;
using FastSoap.Benchmarks.Contracts;

namespace FastSoap.Benchmarks.Services
{

    /// <summary>
    /// Deterministic, allocation-minimal business logic implementations for Echo and Transfer across
    /// FastSoap, CoreWCF, and SoapCore.
    /// </summary>
    public static class BenchmarkPayloads
    {

        #region Fields

        /// <summary>
        /// Canonical SOAP 1.1 <c>EchoString</c> request payload matching <c>echo-xmlser.request.xml</c>.
        /// </summary>
        public static readonly byte[] EchoSoap11RequestUtf8 =
            "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>hello-fastsoap</echo></EchoString></s:Body></s:Envelope>"u8.ToArray();

        /// <summary>
        /// Fixed UTC settlement timestamp matching the R0-G payment fixtures.
        /// </summary>
        public static readonly DateTime FixedSettlementUtc = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Quoted SOAP 1.1 <c>SOAPAction</c> header value for <c>EchoString</c>.
        /// </summary>
        public const string QuotedEchoAction = "\"" + BenchmarkConstants.EchoAction + "\"";

        /// <summary>
        /// Quoted SOAP 1.1 <c>SOAPAction</c> header value for <c>Transfer</c>.
        /// </summary>
        public const string QuotedTransferAction = "\"" + BenchmarkConstants.TransferAction + "\"";

        /// <summary>
        /// Shared strict <see cref="BenchmarkXmlContext"/> configured per ADR-001.
        /// </summary>
        public static readonly BenchmarkXmlContext SharedXmlContext = new BenchmarkXmlContext(new XmlSerializerOptions
        {
            AllowDuplicateProperties = false,
            OmitXmlDeclaration = true,
            UnmappedMemberHandling = XmlUnmappedMemberHandling.Disallow
        });

        /// <summary>
        /// Canonical SOAP 1.1 <c>Transfer</c> request payload matching <c>transfer-xmlser-urnpayments.request.xml</c>.
        /// </summary>
        public static readonly byte[] TransferSoap11RequestUtf8 =
            "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><Transfer xmlns=\"urn:payments\"><request><AccountNumber>123456</AccountNumber><Amount>100.50</Amount><Currency>USD</Currency><Metadata><CorrelationId>CORR-2026-0001</CorrelationId><Reference>INV-42</Reference></Metadata><Note>Invoice settlement</Note></request></Transfer></s:Body></s:Envelope>"u8.ToArray();

        #endregion

        #region Public Methods

        /// <summary>
        /// Creates the manual <see cref="SoapOperationDescriptor"/> for <c>EchoString</c>.
        /// </summary>
        /// <returns>An immutable <see cref="SoapOperationDescriptor"/>.</returns>
        public static SoapOperationDescriptor CreateManualEchoDescriptor()
        {
            return new SoapOperationDescriptor(
                "EchoString",
                BenchmarkConstants.EchoAction,
                BenchmarkConstants.EchoReplyAction,
                new SoapQName(BenchmarkConstants.EchoNamespace, "EchoString"),
                new SoapQName(BenchmarkConstants.EchoNamespace, "EchoStringResponse"));
        }

        /// <summary>
        /// Creates the manual <see cref="SoapOperationDescriptor"/> for <c>Transfer</c>.
        /// </summary>
        /// <returns>An immutable <see cref="SoapOperationDescriptor"/>.</returns>
        public static SoapOperationDescriptor CreateManualTransferDescriptor()
        {
            return new SoapOperationDescriptor(
                "Transfer",
                BenchmarkConstants.TransferAction,
                BenchmarkConstants.TransferReplyAction,
                new SoapQName(BenchmarkConstants.PaymentsNamespace, "Transfer"),
                new SoapQName(BenchmarkConstants.PaymentsNamespace, "TransferResponse"));
        }

        /// <summary>
        /// Executes the shared deterministic Transfer result mapping.
        /// </summary>
        /// <param name="request">The validated transfer request.</param>
        /// <returns>The deterministic <see cref="TransferResult"/>.</returns>
        public static TransferResult ExecuteTransfer(TransferRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            bool ok = request.Amount > 0m;

            return new TransferResult
            {
                TransactionId = "TX-001",
                Successful = ok,
                ProcessedAtUtc = FixedSettlementUtc,
                Status = ok ? PaymentStatus.Completed : PaymentStatus.Rejected
            };
        }

        #endregion

    }

    /// <summary>
    /// FastSoap service implementation for generated Echo registration.
    /// </summary>
    public sealed class BenchmarkEchoService : IBenchmarkEchoService
    {

        #region Public Methods

        /// <inheritdoc />
        public Task<EchoStringResponseWire> EchoStringAsync(EchoStringRequestWire request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(new EchoStringResponseWire
            {
                EchoStringResult = request.Echo
            });
        }

        #endregion

    }

    /// <summary>
    /// FastSoap service implementation for generated Transfer registration.
    /// </summary>
    public sealed class BenchmarkPaymentService : IBenchmarkPaymentService
    {

        #region Public Methods

        /// <inheritdoc />
        public Task<TransferResult> TransferAsync(TransferRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(BenchmarkPayloads.ExecuteTransfer(request));
        }

        #endregion

    }

    /// <summary>
    /// Explicit zero-allocation <see cref="IServiceProvider"/> for in-process benchmarks.
    /// </summary>
    public sealed class BenchmarkServiceProvider : IServiceProvider
    {

        #region Fields

        private readonly IBenchmarkEchoService _echoService;
        private readonly IBenchmarkPaymentService _paymentService;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new <see cref="BenchmarkServiceProvider"/>.
        /// </summary>
        /// <param name="echoService">The echo service instance.</param>
        /// <param name="paymentService">The payment service instance.</param>
        public BenchmarkServiceProvider(
            IBenchmarkEchoService echoService,
            IBenchmarkPaymentService paymentService)
        {
            ArgumentNullException.ThrowIfNull(echoService);
            ArgumentNullException.ThrowIfNull(paymentService);

            _echoService = echoService;
            _paymentService = paymentService;
        }

        #endregion

        #region Public Methods

        /// <inheritdoc />
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IBenchmarkEchoService))
            {
                return _echoService;
            }

            if (serviceType == typeof(IBenchmarkPaymentService))
            {
                return _paymentService;
            }

            return null;
        }

        #endregion

    }

    /// <summary>
    /// CoreWCF <c>DataContractSerializer</c> Echo service implementation.
    /// </summary>
    [ServiceBehavior(
        IncludeExceptionDetailInFaults = false,
        InstanceContextMode = InstanceContextMode.Single,
        ConcurrencyMode = ConcurrencyMode.Multiple)]
    public sealed class CoreWcfEchoDcsService : ICoreWcfEchoDcsService
    {

        #region Public Methods

        /// <inheritdoc />
        public Task<string?> EchoStringAsync(string? echo)
        {
            return Task.FromResult(echo);
        }

        #endregion

    }

    /// <summary>
    /// CoreWCF <c>XmlSerializerFormat</c> Echo service implementation.
    /// </summary>
    [ServiceBehavior(
        IncludeExceptionDetailInFaults = false,
        InstanceContextMode = InstanceContextMode.Single,
        ConcurrencyMode = ConcurrencyMode.Multiple)]
    public sealed class CoreWcfEchoXmlService : ICoreWcfEchoXmlService
    {

        #region Public Methods

        /// <inheritdoc />
        public Task<string?> EchoStringAsync(string? echo)
        {
            return Task.FromResult(echo);
        }

        #endregion

    }

    /// <summary>
    /// CoreWCF <c>DataContractSerializer</c> payment service implementation.
    /// </summary>
    [ServiceBehavior(
        IncludeExceptionDetailInFaults = false,
        InstanceContextMode = InstanceContextMode.Single,
        ConcurrencyMode = ConcurrencyMode.Multiple)]
    public sealed class CoreWcfPaymentDcsService : ICoreWcfPaymentDcsService
    {

        #region Public Methods

        /// <inheritdoc />
        public Task<TransferResult> TransferAsync(TransferRequest request)
        {
            return Task.FromResult(BenchmarkPayloads.ExecuteTransfer(request));
        }

        #endregion

    }

    /// <summary>
    /// CoreWCF <c>XmlSerializerFormat</c> payment service implementation.
    /// </summary>
    [ServiceBehavior(
        IncludeExceptionDetailInFaults = false,
        InstanceContextMode = InstanceContextMode.Single,
        ConcurrencyMode = ConcurrencyMode.Multiple)]
    public sealed class CoreWcfPaymentXmlService : ICoreWcfPaymentXmlService
    {

        #region Public Methods

        /// <inheritdoc />
        public Task<TransferResult> TransferAsync(TransferRequest request)
        {
            return Task.FromResult(BenchmarkPayloads.ExecuteTransfer(request));
        }

        #endregion

    }

    /// <summary>
    /// SoapCore Echo service implementation.
    /// </summary>
    public sealed class SoapCoreEchoService : ISoapCoreEchoService
    {

        #region Public Methods

        /// <inheritdoc />
        public Task<string?> EchoStringAsync(string? echo)
        {
            return Task.FromResult(echo);
        }

        #endregion

    }

    /// <summary>
    /// SoapCore payment service implementation.
    /// </summary>
    public sealed class SoapCorePaymentService : ISoapCorePaymentService
    {

        #region Public Methods

        /// <inheritdoc />
        public Task<TransferResult> TransferAsync(TransferRequest request)
        {
            return Task.FromResult(BenchmarkPayloads.ExecuteTransfer(request));
        }

        #endregion

    }

}
