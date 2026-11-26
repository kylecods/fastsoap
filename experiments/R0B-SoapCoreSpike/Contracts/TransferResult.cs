using System.Xml.Serialization;

namespace R0B.SoapCoreSpike.Contracts
{
    /// <summary>
    /// Represents the payment transfer result payload under the <c>urn:payments</c> contract namespace.
    /// </summary>
    [XmlRoot("TransferResult", Namespace = "urn:payments")]
    public sealed class TransferResult
    {
        #region Properties

        /// <summary>
        /// Gets or sets a value indicating whether the transfer succeeded.
        /// </summary>
        public bool Successful { get; set; }

        /// <summary>
        /// Gets or sets the assigned transaction identifier.
        /// </summary>
        public string TransactionId { get; set; } = string.Empty;

        #endregion
    }
}
