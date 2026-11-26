using System.Xml.Serialization;

namespace R0B.SoapCoreSpike.Contracts
{
    /// <summary>
    /// Represents a payment transfer request payload under the <c>urn:payments</c> contract namespace.
    /// </summary>
    [XmlRoot("request", Namespace = "urn:payments")]
    public sealed class TransferRequest
    {
        #region Properties

        /// <summary>
        /// Gets or sets the source or destination account number.
        /// </summary>
        public string AccountNumber { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the monetary transfer amount.
        /// </summary>
        public decimal Amount { get; set; }

        #endregion
    }
}
