using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;

using CloudNimble.Text.Xml.Serialization;

using FastSoap.Abstractions;
using FastSoap.Metadata;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FastSoap.IntegrationTests
{

    public enum IntegrationPaymentStatus
    {
        Pending = 0,
        Completed = 1,
        Rejected = 2
    }

    [XmlRoot(ElementName = "Echo", Namespace = "urn:echo")]
    public sealed class EchoRequest
    {

        #region Properties

        [XmlElement(ElementName = "Message", Namespace = "urn:echo")]
        public required string Message { get; init; }

        #endregion

    }

    [XmlRoot(ElementName = "EchoResponse", Namespace = "urn:echo")]
    public sealed class EchoResponse
    {

        #region Properties

        [XmlElement(ElementName = "EchoResult", Namespace = "urn:echo")]
        public required string EchoResult { get; init; }

        #endregion

    }

    [SoapService(Name = "IntegrationPaymentService", Namespace = "urn:payments")]
    [SoapWireContract(typeof(IntegrationXmlContext))]
    public interface IIntegrationPaymentService
    {

        #region Public Methods

        [SoapOperation(
            Action = "urn:payments/IPaymentService/Transfer",
            ReplyAction = "urn:payments/IPaymentService/TransferResponse")]
        [SoapWireContract(
            RequestType = typeof(TransferRequestWire),
            RequestMemberName = nameof(TransferRequestWire.Request),
            ResponseType = typeof(TransferResponseWire),
            ResponseMemberName = nameof(TransferResponseWire.Result))]
        Task<TransferResult> TransferAsync(TransferRequest request, CancellationToken cancellationToken);

        #endregion

    }

    public sealed class IntegrationPaymentService : IIntegrationPaymentService
    {

        #region Fields

        private static readonly DateTime FixedProcessedUtc = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);
        private readonly InvocationCounter _counter;

        #endregion

        #region Constructors

        public IntegrationPaymentService(InvocationCounter counter)
        {
            ArgumentNullException.ThrowIfNull(counter);

            _counter = counter;
        }

        #endregion

        #region Public Methods

        public Task<TransferResult> TransferAsync(TransferRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            _counter.IncrementTransfer();

            return Task.FromResult(new TransferResult
            {
                TransactionId = $"TX-{request.AccountNumber}",
                Successful = request.Amount > 0m,
                ProcessedAtUtc = FixedProcessedUtc,
                Status = request.Amount > 0m ? IntegrationPaymentStatus.Completed : IntegrationPaymentStatus.Rejected
            });
        }

        #endregion

    }

    [XmlSerializable<EchoRequest>]
    [XmlSerializable<EchoResponse>]
    [XmlSerializable<TransferRequestWire>]
    [XmlSerializable<TransferResponseWire>]
    public partial class IntegrationXmlContext : XmlSerializerContext
    {
    }

    public sealed class InvocationCounter
    {

        #region Fields

        private int _echoCount;
        private int _transferCount;

        #endregion

        #region Properties

        public int EchoCount => Volatile.Read(ref _echoCount);

        public int TransferCount => Volatile.Read(ref _transferCount);

        #endregion

        #region Public Methods

        public void IncrementEcho()
        {
            Interlocked.Increment(ref _echoCount);
        }

        public void IncrementTransfer()
        {
            Interlocked.Increment(ref _transferCount);
        }

        #endregion

    }

    public sealed class RecordingSoapHeaderHandler : ISoapHeaderHandler
    {

        #region Fields

        private readonly List<SoapHeaderValue> _captured = new List<SoapHeaderValue>();

        #endregion

        #region Properties

        public IReadOnlyList<SoapHeaderValue> Captured => _captured;

        public SoapQName HeaderName { get; }

        #endregion

        #region Constructors

        public RecordingSoapHeaderHandler(SoapQName headerName)
        {
            HeaderName = headerName;
        }

        #endregion

        #region Public Methods

        public ValueTask HandleAsync(
            SoapHeaderValue header,
            SoapCallContext context,
            CancellationToken cancellationToken)
        {
            _captured.Add(header);

            return ValueTask.CompletedTask;
        }

        #endregion

    }

    public sealed class TestBearerAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {

        #region Fields

        public const string SchemeName = "TestBearer";

        #endregion

        #region Constructors

        public TestBearerAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        #endregion

        #region Protected Methods

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string? header = Request.Headers.Authorization;
            if (string.IsNullOrWhiteSpace(header))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            if (string.Equals(header, "Bearer valid-payment-user", StringComparison.Ordinal))
            {
                Claim[] claims =
                [
                    new Claim(ClaimTypes.Name, "alice"),
                    new Claim(ClaimTypes.Role, "PaymentsWriter")
                ];
                ClaimsIdentity identity = new ClaimsIdentity(claims, SchemeName);

                return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
            }

            if (string.Equals(header, "Bearer readonly-user", StringComparison.Ordinal))
            {
                Claim[] claims =
                [
                    new Claim(ClaimTypes.Name, "bob"),
                    new Claim(ClaimTypes.Role, "ReaderOnly")
                ];
                ClaimsIdentity identity = new ClaimsIdentity(claims, SchemeName);

                return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
            }

            return Task.FromResult(AuthenticateResult.Fail("Invalid bearer token."));
        }

        #endregion

    }

    public sealed class TransferMetadata
    {

        #region Properties

        [XmlElement(ElementName = "CorrelationId", Namespace = "urn:payments", Order = 0)]
        public string CorrelationId { get; set; } = string.Empty;

        [XmlElement(ElementName = "Reference", Namespace = "urn:payments", IsNullable = true, Order = 1)]
        public string? Reference { get; set; }

        #endregion

    }

    public sealed class TransferRequest
    {

        #region Properties

        [XmlElement(ElementName = "AccountNumber", Namespace = "urn:payments", Order = 0)]
        public required string AccountNumber { get; init; }

        [XmlElement(ElementName = "Amount", Namespace = "urn:payments", Order = 1)]
        public decimal Amount { get; init; }

        [XmlElement(ElementName = "Currency", Namespace = "urn:payments", Order = 2)]
        public string Currency { get; init; } = "USD";

        [XmlElement(ElementName = "Metadata", Namespace = "urn:payments", IsNullable = true, Order = 3)]
        public TransferMetadata? Metadata { get; init; }

        [XmlElement(ElementName = "Note", Namespace = "urn:payments", IsNullable = true, Order = 4)]
        public string? Note { get; init; }

        #endregion

    }

    [XmlRoot(ElementName = "Transfer", Namespace = "urn:payments")]
    public sealed class TransferRequestWire
    {

        #region Properties

        [XmlElement(ElementName = "request", Namespace = "urn:payments")]
        public required TransferRequest Request { get; init; }

        #endregion

    }

    [XmlRoot(ElementName = "TransferResponse", Namespace = "urn:payments")]
    public sealed class TransferResponseWire
    {

        #region Properties

        [XmlElement(ElementName = "TransferResult", Namespace = "urn:payments")]
        public required TransferResult Result { get; init; }

        #endregion

    }

    public sealed class TransferResult
    {

        #region Properties

        [XmlElement(ElementName = "ProcessedAtUtc", Namespace = "urn:payments", Order = 2)]
        public DateTime ProcessedAtUtc { get; init; }

        [XmlElement(ElementName = "Status", Namespace = "urn:payments", Order = 3)]
        public IntegrationPaymentStatus Status { get; init; }

        [XmlElement(ElementName = "Successful", Namespace = "urn:payments", Order = 1)]
        public bool Successful { get; init; }

        [XmlElement(ElementName = "TransactionId", Namespace = "urn:payments", Order = 0)]
        public required string TransactionId { get; init; }

        #endregion

    }

    internal static class IntegrationMetadataFactory
    {

        #region Internal Methods

        internal static SoapServiceMetadata CreatePaymentMetadata()
        {
            SoapSchemaType metadataType = SoapSchemaType.Complex(
                new SoapQName("urn:payments", "TransferMetadata"),
                [
                    SoapSchemaElement.CreateMember("CorrelationId", SoapSchemaType.String),
                    SoapSchemaElement.CreateMember("Reference", SoapSchemaType.String, minOccurs: 0, isNillable: true)
                ]);

            SoapSchemaType statusType = SoapSchemaType.Enum(
                new SoapQName("urn:payments", "IntegrationPaymentStatus"),
                ["Pending", "Completed", "Rejected"]);

            SoapSchemaType requestType = SoapSchemaType.Complex(
                new SoapQName("urn:payments", "TransferRequest"),
                [
                    SoapSchemaElement.CreateMember("AccountNumber", SoapSchemaType.String),
                    SoapSchemaElement.CreateMember("Amount", SoapSchemaType.Decimal),
                    SoapSchemaElement.CreateMember("Currency", SoapSchemaType.String),
                    SoapSchemaElement.CreateMember("Metadata", metadataType, minOccurs: 0, isNillable: true),
                    SoapSchemaElement.CreateMember("Note", SoapSchemaType.String, minOccurs: 0, isNillable: true)
                ]);

            SoapSchemaType resultType = SoapSchemaType.Complex(
                new SoapQName("urn:payments", "TransferResult"),
                [
                    SoapSchemaElement.CreateMember("TransactionId", SoapSchemaType.String),
                    SoapSchemaElement.CreateMember("Successful", SoapSchemaType.Boolean),
                    SoapSchemaElement.CreateMember("ProcessedAtUtc", SoapSchemaType.DateTime),
                    SoapSchemaElement.CreateMember("Status", statusType)
                ]);

            SoapOperationMetadata transferOp = new SoapOperationMetadata(
                IntegrationPaymentServiceSoapContract.Transfer,
                SoapSchemaElement.CreateWrapped(
                    new SoapQName("urn:payments", "Transfer"),
                    [SoapSchemaElement.CreateMember("request", requestType, minOccurs: 0, isNillable: true)]),
                SoapSchemaElement.CreateWrapped(
                    new SoapQName("urn:payments", "TransferResponse"),
                    [SoapSchemaElement.CreateMember("TransferResult", resultType, minOccurs: 0, isNillable: true)]));

            return new SoapServiceMetadata(
                serviceName: "IntegrationPaymentService",
                targetNamespace: "urn:payments",
                operations: [transferOp],
                supportedVersions: SoapVersionFlags.Both,
                portTypeName: "IIntegrationPaymentService");
        }

        #endregion

    }

}
