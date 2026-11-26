using CloudNimble.Text.Xml.Serialization;
using R0B.SoapCoreSpike.Contracts;

namespace R0B.SoapCoreSpike.Serialization
{
    /// <summary>
    /// Source-generated XmlEssentials serialization context for payment contracts.
    /// </summary>
    /// <remarks>
    /// Note that <c>CloudNimble.Text.Xml.Serialization</c> recognizes <c>XmlEssentials</c> root/element attributes;
    /// when <c>System.Xml.Serialization.XmlRootAttribute</c> is present on the DTOs for SoapCore compatibility,
    /// XmlEssentials' generator ignores <c>System.Xml.Serialization</c> attributes unless XmlEssentials' own attributes are used.
    /// </remarks>
    [XmlSourceGenerationOptions(OmitXmlDeclaration = true, DefaultNamespace = "urn:payments")]
    [XmlSerializable<TransferRequestWire>]
    [XmlSerializable<TransferResultWire>]
    [XmlSerializable<EchoMessageWire>]
    public partial class PaymentXmlContext : XmlSerializerContext
    {
    }

    /// <summary>
    /// Explicit XmlEssentials-attributed DTO matching the nested <c>&lt;request&gt;</c> parameter element.
    /// </summary>
    [CloudNimble.Text.Xml.Serialization.XmlRoot("request", Namespace = "urn:payments")]
    public sealed class TransferRequestWire
    {
        #region Properties

        /// <summary>
        /// Gets or sets the account number.
        /// </summary>
        public string AccountNumber { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the transfer amount.
        /// </summary>
        public decimal Amount { get; set; }

        #endregion

        #region Public Methods

        /// <summary>
        /// Converts this wire DTO to the SoapCore contract <see cref="TransferRequest"/>.
        /// </summary>
        /// <returns>A populated <see cref="TransferRequest"/> instance.</returns>
        public TransferRequest ToContract()
        {
            TransferRequest contract = new TransferRequest
            {
                AccountNumber = AccountNumber,
                Amount = Amount,
            };

            return contract;
        }

        #endregion
    }

    /// <summary>
    /// Explicit XmlEssentials-attributed DTO matching the <c>&lt;TransferResult&gt;</c> response element.
    /// </summary>
    [CloudNimble.Text.Xml.Serialization.XmlRoot("TransferResult", Namespace = "urn:payments")]
    public sealed class TransferResultWire
    {
        #region Properties

        /// <summary>
        /// Gets or sets a value indicating whether the transfer succeeded.
        /// </summary>
        public bool Successful { get; set; }

        /// <summary>
        /// Gets or sets the transaction identifier.
        /// </summary>
        public string TransactionId { get; set; } = string.Empty;

        #endregion
    }

    /// <summary>
    /// Explicit XmlEssentials-attributed DTO matching the <c>&lt;message&gt;</c> Echo parameter element.
    /// </summary>
    [CloudNimble.Text.Xml.Serialization.XmlRoot("message", Namespace = "urn:payments")]
    public sealed class EchoMessageWire
    {
        #region Properties

        /// <summary>
        /// Gets or sets the inner text content of the <c>&lt;message&gt;</c> element.
        /// </summary>
        [CloudNimble.Text.Xml.Serialization.XmlText]
        public string Value { get; set; } = string.Empty;

        #endregion
    }
}
