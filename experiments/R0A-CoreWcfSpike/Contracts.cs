using System.Runtime.Serialization;
using System.Threading.Tasks;

using CoreWCF;

namespace R0A.CoreWcfSpike
{
    /// <summary>
    /// Defines the Echo SOAP 1.1 service contract used across all R0 research spikes.
    /// </summary>
    [ServiceContract(Namespace = "urn:echo")]
    public interface IEchoService
    {
        /// <summary>
        /// Echoes the supplied message string back to the caller.
        /// </summary>
        /// <param name="message">The input message to echo.</param>
        /// <returns>A task containing the echoed message.</returns>
        [OperationContract(
            Name = "Echo",
            Action = "urn:echo/IEchoService/Echo",
            ReplyAction = "urn:echo/IEchoService/EchoResponse")]
        Task<string> EchoAsync(string message);
    }

    /// <summary>
    /// Defines the nested Payment Transfer SOAP 1.1 service contract in namespace urn:payments.
    /// </summary>
    [ServiceContract(Namespace = "urn:payments")]
    public interface IPaymentService
    {
        /// <summary>
        /// Executes a payment transfer for the specified request payload.
        /// </summary>
        /// <param name="request">The nested transfer request payload.</param>
        /// <returns>A task containing the transfer result.</returns>
        [OperationContract(
            Name = "Transfer",
            Action = "urn:payments/IPaymentService/Transfer",
            ReplyAction = "urn:payments/IPaymentService/TransferResponse")]
        Task<TransferResult> TransferAsync(TransferRequest request);
    }

    /// <summary>
    /// Represents the nested transfer request DTO under urn:payments.
    /// </summary>
    [DataContract(Namespace = "urn:payments", Name = "TransferRequest")]
    public sealed class TransferRequest
    {
        #region Properties

        /// <summary>
        /// Gets or sets the source or destination account number.
        /// </summary>
        [DataMember(Order = 1, IsRequired = true)]
        public string AccountNumber { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the monetary transfer amount.
        /// </summary>
        [DataMember(Order = 2, IsRequired = true)]
        public decimal Amount { get; set; }

        #endregion
    }

    /// <summary>
    /// Represents the nested transfer result DTO under urn:payments.
    /// </summary>
    [DataContract(Namespace = "urn:payments", Name = "TransferResult")]
    public sealed class TransferResult
    {
        #region Properties

        /// <summary>
        /// Gets or sets a value indicating whether the transfer succeeded.
        /// </summary>
        [DataMember(Order = 2, IsRequired = true)]
        public bool Successful { get; set; }

        /// <summary>
        /// Gets or sets the assigned transaction identifier.
        /// </summary>
        [DataMember(Order = 1, IsRequired = true)]
        public string TransactionId { get; set; } = string.Empty;

        #endregion
    }
}
