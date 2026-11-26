using CloudNimble.Text.Xml.Serialization;

namespace FastSoap.R0C.GreenfieldSpike.Contracts
{

    /// <summary>
    /// Represents the SOAP 1.1 Echo request body element.
    /// </summary>
    [XmlRoot(ElementName = "Echo", Namespace = "urn:echo")]
    public sealed class EchoRequest
    {

        #region Properties

        /// <summary>
        /// Gets or sets the message to echo back.
        /// </summary>
        [XmlElement(ElementName = "Message", Namespace = "urn:echo")]
        public required string Message { get; init; }

        #endregion

    }

    /// <summary>
    /// Represents the SOAP 1.1 Echo response body element.
    /// </summary>
    [XmlRoot(ElementName = "EchoResponse", Namespace = "urn:echo")]
    public sealed class EchoResponse
    {

        #region Properties

        /// <summary>
        /// Gets or sets the echoed message result.
        /// </summary>
        [XmlElement(ElementName = "EchoResult", Namespace = "urn:echo")]
        public required string EchoResult { get; init; }

        #endregion

    }

    /// <summary>
    /// Source-generated XML serialization context for the R0-C greenfield spike contracts.
    /// </summary>
    [XmlSerializable<EchoRequest>]
    [XmlSerializable<EchoResponse>]
    [XmlSerializable<TransferRequestWire>]
    [XmlSerializable<TransferResponseWire>]
    public partial class SpikeXmlContext : XmlSerializerContext
    {
    }

    /// <summary>
    /// Represents the nested payment transfer request DTO.
    /// </summary>
    public sealed class TransferRequest
    {

        #region Properties

        /// <summary>
        /// Gets or sets the source or destination account number.
        /// </summary>
        [XmlElement(ElementName = "AccountNumber", Namespace = "urn:payments", Order = 0)]
        public required string AccountNumber { get; init; }

        /// <summary>
        /// Gets or sets the transfer amount.
        /// </summary>
        [XmlElement(ElementName = "Amount", Namespace = "urn:payments", Order = 1)]
        public decimal Amount { get; init; }

        #endregion

    }

    /// <summary>
    /// Represents the document/literal wrapped Transfer request wire element.
    /// </summary>
    [XmlRoot(ElementName = "Transfer", Namespace = "urn:payments")]
    public sealed class TransferRequestWire
    {

        #region Properties

        /// <summary>
        /// Gets or sets the nested transfer request payload.
        /// </summary>
        [XmlElement(ElementName = "request", Namespace = "urn:payments")]
        public required TransferRequest Request { get; init; }

        #endregion

    }

    /// <summary>
    /// Represents the document/literal wrapped Transfer response wire element.
    /// </summary>
    [XmlRoot(ElementName = "TransferResponse", Namespace = "urn:payments")]
    public sealed class TransferResponseWire
    {

        #region Properties

        /// <summary>
        /// Gets or sets the nested transfer result payload.
        /// </summary>
        [XmlElement(ElementName = "TransferResult", Namespace = "urn:payments")]
        public required TransferResult Result { get; init; }

        #endregion

    }

    /// <summary>
    /// Represents the nested payment transfer result DTO.
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
        /// Gets or sets the generated transaction identifier.
        /// </summary>
        [XmlElement(ElementName = "TransactionId", Namespace = "urn:payments", Order = 0)]
        public required string TransactionId { get; init; }

        #endregion

    }

}
