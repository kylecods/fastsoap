using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using CoreWCF;

using FastSoap.R0G.Oracle.Contracts;

namespace FastSoap.R0G.Oracle.Services
{
    /// <summary>
    /// CoreWCF DataContractSerializer Echo service implementation.
    /// </summary>
    [ServiceBehavior(IncludeExceptionDetailInFaults = false)]
    public sealed class EchoDcsService : IEchoDcsService
    {
        #region Public Methods

        /// <inheritdoc />
        public Task<string?> EchoStringAsync(string? echo)
        {
            return Task.FromResult(echo);
        }

        #endregion
    }

    /// <summary>
    /// CoreWCF XmlSerializer Echo service implementation.
    /// </summary>
    [ServiceBehavior(IncludeExceptionDetailInFaults = false)]
    public sealed class EchoXmlSerializerService : IEchoXmlSerializerService
    {
        #region Public Methods

        /// <inheritdoc />
        public Task<string?> EchoStringAsync(string? echo)
        {
            return Task.FromResult(echo);
        }

        #endregion
    }

    /// <summary>
    /// CoreWCF service implementation with SOAP headers and body content.
    /// </summary>
    [ServiceBehavior(IncludeExceptionDetailInFaults = false)]
    public sealed class EchoWithHeadersService : IEchoWithHeadersService
    {
        #region Public Methods

        /// <inheritdoc />
        public Task<EchoHeaderResponseMessage> EchoWithMessageContractAsync(EchoHeaderRequestMessage request)
        {
            ArgumentNullException.ThrowIfNull(request);

            EchoHeaderResponseMessage response = new EchoHeaderResponseMessage
            {
                SayHello = "Saying Hello " + request.Text,
                SayHi = "Saying Hi " + request.Text
            };

            return Task.FromResult(response);
        }

        #endregion
    }

    /// <summary>
    /// CoreWCF default-namespace DataContractSerializer payment service implementation.
    /// </summary>
    [ServiceBehavior(IncludeExceptionDetailInFaults = false)]
    public sealed class PaymentDcsDefaultService : IPaymentDcsDefaultService
    {
        #region Public Methods

        /// <inheritdoc />
        public Task<TransferResultDefaultDcs> TransferAsync(TransferRequestDefaultDcs request)
        {
            ArgumentNullException.ThrowIfNull(request);

            TransferResultDefaultDcs result = new TransferResultDefaultDcs
            {
                Successful = true,
                TransactionId = "TX-" + request.AccountNumber
            };

            return Task.FromResult(result);
        }

        #endregion
    }

    /// <summary>
    /// CoreWCF explicit <c>urn:payments</c> DataContractSerializer payment service implementation.
    /// </summary>
    [ServiceBehavior(IncludeExceptionDetailInFaults = false)]
    public sealed class PaymentDcsUrnService : IPaymentDcsUrnService
    {
        #region Fields

        private static readonly DateTime s_fixedTimestampUtc = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);

        #endregion

        #region Public Methods

        /// <inheritdoc />
        public Task<BatchTransferResult> BatchTransferAsync(BatchTransferRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            List<TransferResult> results = [];
            for (int index = 0; index < request.Items.Count; index++)
            {
                TransferRequest item = request.Items[index];
                results.Add(new TransferResult
                {
                    ProcessedAtUtc = s_fixedTimestampUtc,
                    Status = PaymentStatus.Completed,
                    Successful = true,
                    TransactionId = $"TX-{request.BatchId}-{index + 1:D3}"
                });
            }

            BatchTransferResult batchResult = new BatchTransferResult
            {
                BatchId = request.BatchId,
                Results = results
            };

            return Task.FromResult(batchResult);
        }

        /// <inheritdoc />
        public Task<TransferResult> TransferAsync(TransferRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (request.Amount < 0m)
            {
                PaymentFaultDetail detail = new PaymentFaultDetail
                {
                    ErrorCode = "INVALID_AMOUNT",
                    Message = "Transfer amount must be non-negative."
                };

                throw new FaultException<PaymentFaultDetail>(
                    detail,
                    new FaultReason("Payment validation failed."),
                    new FaultCode("Sender"),
                    PaymentConstants.FaultAction);
            }

            if (string.Equals(request.AccountNumber, "FAIL-INTERNAL", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Simulated internal backend database failure with sensitive connection string.");
            }

            TransferResult result = new TransferResult
            {
                ProcessedAtUtc = s_fixedTimestampUtc,
                Status = PaymentStatus.Completed,
                Successful = true,
                TransactionId = "TX-001"
            };

            return Task.FromResult(result);
        }

        #endregion
    }

    /// <summary>
    /// CoreWCF <c>XmlSerializerFormat</c> payment service implementation.
    /// </summary>
    [ServiceBehavior(IncludeExceptionDetailInFaults = false)]
    public sealed class PaymentXmlSerializerService : IPaymentXmlSerializerService
    {
        #region Fields

        private static readonly DateTime s_fixedTimestampUtc = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);

        #endregion

        #region Public Methods

        /// <inheritdoc />
        public Task<TransferResult> TransferAsync(TransferRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            TransferResult result = new TransferResult
            {
                ProcessedAtUtc = s_fixedTimestampUtc,
                Status = PaymentStatus.Completed,
                Successful = true,
                TransactionId = "TX-001"
            };

            return Task.FromResult(result);
        }

        #endregion
    }
}
