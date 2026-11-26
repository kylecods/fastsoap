using System;
using System.Threading.Tasks;

namespace R0A.CoreWcfSpike
{
    /// <summary>
    /// Implements the Echo SOAP 1.1 service contract.
    /// </summary>
    public sealed class EchoService : IEchoService
    {
        #region Public Methods

        /// <inheritdoc />
        public Task<string> EchoAsync(string message)
        {
            ArgumentNullException.ThrowIfNull(message);

            return Task.FromResult(message);
        }

        #endregion
    }

    /// <summary>
    /// Implements the nested Payment Transfer SOAP 1.1 service contract.
    /// </summary>
    public sealed class PaymentService : IPaymentService
    {
        #region Public Methods

        /// <inheritdoc />
        public Task<TransferResult> TransferAsync(TransferRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            TransferResult result = new TransferResult
            {
                TransactionId = $"TX-{request.AccountNumber}",
                Successful = request.Amount > 0m,
            };

            return Task.FromResult(result);
        }

        #endregion
    }
}
