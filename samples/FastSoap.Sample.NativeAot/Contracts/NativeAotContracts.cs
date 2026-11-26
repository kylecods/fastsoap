using System;
using System.Threading;
using System.Threading.Tasks;

using CloudNimble.Text.Xml.Serialization;

using FastSoap.Abstractions;

namespace FastSoap.Sample.NativeAot.Contracts
{

    /// <summary>
    /// Document/literal Echo request payload.
    /// </summary>
    [XmlRoot(ElementName = "Echo", Namespace = "urn:echo")]
    public sealed class EchoRequest
    {

        #region Properties

        /// <summary>
        /// Gets or sets the input message.
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
    /// Contract-first SOAP service for Native AOT validation.
    /// </summary>
    [SoapService(Name = "NativePaymentService", Namespace = "urn:payments")]
    [SoapWireContract(typeof(NativeAotXmlContext))]
    public interface INativePaymentService
    {

        #region Public Methods

        /// <summary>
        /// Processes a nested payment transfer request.
        /// </summary>
        /// <param name="request">The transfer request.</param>
        /// <param name="cancellationToken">Request cancellation token.</param>
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
    /// Source-generated XML serialization context with reflection disabled.
    /// </summary>
    [XmlSerializable<EchoRequest>]
    [XmlSerializable<EchoResponse>]
    [XmlSerializable<TransferRequestWire>]
    [XmlSerializable<TransferResponseWire>]
    public partial class NativeAotXmlContext : XmlSerializerContext
    {
    }

    /// <summary>
    /// Nested payment transfer request DTO.
    /// </summary>
    public sealed class TransferRequest
    {

        #region Properties

        /// <summary>
        /// Gets or sets the source account identifier.
        /// </summary>
        [XmlElement(ElementName = "AccountNumber", Namespace = "urn:payments", Order = 0)]
        public required string AccountNumber { get; init; }

        /// <summary>
        /// Gets or sets the monetary transfer amount.
        /// </summary>
        [XmlElement(ElementName = "Amount", Namespace = "urn:payments", Order = 1)]
        public decimal Amount { get; init; }

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
        /// Gets or sets the nested transfer request.
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
        /// Gets or sets the nested transfer result.
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
