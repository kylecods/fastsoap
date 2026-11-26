using System;
using System.Threading;
using System.Threading.Tasks;

using CloudNimble.Text.Xml.Serialization;

using FastSoap.Abstractions;

namespace FastSoap.Sample.Payments.Contracts
{

    /// <summary>
    /// Payment status emitted in <c>TransferResult</c>.
    /// </summary>
    public enum PaymentStatus
    {
        Pending = 0,
        Completed = 1,
        Rejected = 2
    }

    /// <summary>
    /// Document/literal Echo request payload.
    /// </summary>
    [XmlRoot(ElementName = "Echo", Namespace = "urn:echo")]
    public sealed class EchoRequest
    {

        #region Properties

        /// <summary>
        /// Gets or sets the input message to echo.
        /// </summary>
        [XmlElement(ElementName = "Message", Namespace = "urn:echo")]
        public required string Message { get; init; }

        #endregion

    }

    /// <summary>
    /// Document/literal Echo response payload.
    /// </summary>
    [XmlRoot(ElementName = "EchoResponse", Namespace = "urn:echo")]
    public sealed class EchoResponse
    {

        #region Properties

        /// <summary>
        /// Gets or sets the echoed output message.
        /// </summary>
        [XmlElement(ElementName = "EchoResult", Namespace = "urn:echo")]
        public required string EchoResult { get; init; }

        #endregion

    }

    /// <summary>
    /// Contract-first SOAP payment service interface used by <c>FastSoap.Generators</c>.
    /// </summary>
    [SoapService(Name = "PaymentService", Namespace = "urn:payments")]
    [SoapWireContract(typeof(PaymentXmlContext))]
    public interface IPaymentService
    {

        #region Public Methods

        /// <summary>
        /// Executes a payment transfer request.
        /// </summary>
        /// <param name="request">The nested transfer request.</param>
        /// <param name="cancellationToken">A cancellation token tied to the HTTP request.</param>
        /// <returns>The transfer result.</returns>
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

    /// <summary>
    /// Source-generated XML serializer context for payment and echo wire DTOs.
    /// </summary>
    [XmlSerializable<EchoRequest>]
    [XmlSerializable<EchoResponse>]
    [XmlSerializable<TransferRequestWire>]
    [XmlSerializable<TransferResponseWire>]
    public partial class PaymentXmlContext : XmlSerializerContext
    {
    }

    /// <summary>
    /// Nested payment correlation metadata.
    /// </summary>
    public sealed class TransferMetadata
    {

        #region Properties

        /// <summary>
        /// Gets or sets the correlation identifier.
        /// </summary>
        [XmlElement(ElementName = "CorrelationId", Namespace = "urn:payments", Order = 0)]
        public string CorrelationId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets an optional reference identifier.
        /// </summary>
        [XmlElement(ElementName = "Reference", Namespace = "urn:payments", IsNullable = true, Order = 1)]
        public string? Reference { get; set; }

        #endregion

    }

    /// <summary>
    /// Nested payment transfer request DTO.
    /// </summary>
    public sealed class TransferRequest
    {

        #region Properties

        /// <summary>
        /// Gets or sets the account number.
        /// </summary>
        [XmlElement(ElementName = "AccountNumber", Namespace = "urn:payments", Order = 0)]
        public required string AccountNumber { get; init; }

        /// <summary>
        /// Gets or sets the monetary transfer amount.
        /// </summary>
        [XmlElement(ElementName = "Amount", Namespace = "urn:payments", Order = 1)]
        public decimal Amount { get; init; }

        /// <summary>
        /// Gets or sets the ISO currency code.
        /// </summary>
        [XmlElement(ElementName = "Currency", Namespace = "urn:payments", Order = 2)]
        public string Currency { get; init; } = "USD";

        /// <summary>
        /// Gets or sets optional correlation metadata.
        /// </summary>
        [XmlElement(ElementName = "Metadata", Namespace = "urn:payments", IsNullable = true, Order = 3)]
        public TransferMetadata? Metadata { get; init; }

        /// <summary>
        /// Gets or sets an optional memo note.
        /// </summary>
        [XmlElement(ElementName = "Note", Namespace = "urn:payments", IsNullable = true, Order = 4)]
        public string? Note { get; init; }

        #endregion

    }

    /// <summary>
    /// Document/literal wrapped Transfer request wire element.
    /// </summary>
    [XmlRoot(ElementName = "Transfer", Namespace = "urn:payments")]
    public sealed class TransferRequestWire
    {

        #region Properties

        /// <summary>
        /// Gets or sets the wrapped <see cref="TransferRequest"/> parameter.
        /// </summary>
        [XmlElement(ElementName = "request", Namespace = "urn:payments")]
        public required TransferRequest Request { get; init; }

        #endregion

    }

    /// <summary>
    /// Document/literal wrapped Transfer response wire element.
    /// </summary>
    [XmlRoot(ElementName = "TransferResponse", Namespace = "urn:payments")]
    public sealed class TransferResponseWire
    {

        #region Properties

        /// <summary>
        /// Gets or sets the wrapped <see cref="TransferResult"/> return value.
        /// </summary>
        [XmlElement(ElementName = "TransferResult", Namespace = "urn:payments")]
        public required TransferResult Result { get; init; }

        #endregion

    }

    /// <summary>
    /// Nested payment transfer result DTO.
    /// </summary>
    public sealed class TransferResult
    {

        #region Properties

        /// <summary>
        /// Gets or sets the UTC settlement timestamp.
        /// </summary>
        [XmlElement(ElementName = "ProcessedAtUtc", Namespace = "urn:payments", Order = 2)]
        public DateTime ProcessedAtUtc { get; init; }

        /// <summary>
        /// Gets or sets the resulting payment status.
        /// </summary>
        [XmlElement(ElementName = "Status", Namespace = "urn:payments", Order = 3)]
        public PaymentStatus Status { get; init; }

        /// <summary>
        /// Gets or sets a value indicating whether the transfer succeeded.
        /// </summary>
        [XmlElement(ElementName = "Successful", Namespace = "urn:payments", Order = 1)]
        public bool Successful { get; init; }

        /// <summary>
        /// Gets or sets the assigned transaction identifier.
        /// </summary>
        [XmlElement(ElementName = "TransactionId", Namespace = "urn:payments", Order = 0)]
        public required string TransactionId { get; init; }

        #endregion

    }

}
