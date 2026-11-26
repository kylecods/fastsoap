using System;
using System.Threading.Tasks;
using R0B.SoapCoreSpike.Contracts;

namespace R0B.SoapCoreSpike.Services
{
    /// <summary>
    /// Implements <see cref="IPaymentService"/> for the R0-B SoapCore spike.
    /// </summary>
    public sealed class PaymentService : IPaymentService
    {
        #region Public Methods

        /// <inheritdoc />
        public Task<string> EchoAsync(string message)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(message);

            return Task.FromResult(message);
        }

        /// <inheritdoc />
        public Task<TransferResult> TransferAsync(TransferRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.AccountNumber);

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
