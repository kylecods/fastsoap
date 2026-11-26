using System.ServiceModel;
using System.Threading.Tasks;

namespace R0B.SoapCoreSpike.Contracts
{
    /// <summary>
    /// Defines the SOAP 1.1 payment service contract for the R0-B SoapCore adaptation spike.
    /// </summary>
    [ServiceContract(Namespace = "urn:payments")]
    public interface IPaymentService
    {
        #region Public Methods

        /// <summary>
        /// Echoes the supplied message string.
        /// </summary>
        /// <param name="message">The input message to echo.</param>
        /// <returns>The echoed message.</returns>
        [OperationContract(
            Name = "Echo",
            Action = "urn:payments/IPaymentService/Echo",
            ReplyAction = "urn:payments/IPaymentService/EchoResponse")]
        Task<string> EchoAsync(string message);

        /// <summary>
        /// Executes a payment transfer for the specified request.
        /// </summary>
        /// <param name="request">The nested transfer request payload.</param>
        /// <returns>The transfer execution result.</returns>
        [OperationContract(
            Name = "Transfer",
            Action = "urn:payments/IPaymentService/Transfer",
            ReplyAction = "urn:payments/IPaymentService/TransferResponse")]
        Task<TransferResult> TransferAsync(TransferRequest request);

        #endregion
    }
}
