using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace FastSoap.R0G.Oracle.Contracts
{
    /// <summary>
    /// Shared SOAP constants for the nested <c>urn:payments</c> oracle contract.
    /// </summary>
    public static class PaymentConstants
    {
        #region Fields

        /// <summary>
        /// SOAP action for <c>BatchTransfer</c>.
        /// </summary>
        public const string BatchTransferAction = OperationBase + "BatchTransfer";

        /// <summary>
        /// SOAP reply action for <c>BatchTransfer</c>.
        /// </summary>
        public const string BatchTransferReplyAction = OperationBase + "BatchTransferResponse";

        /// <summary>
        /// Service contract name matching the FastSoap specification.
        /// </summary>
        public const string ContractName = "IPaymentService";

        /// <summary>
        /// Data contract namespace for explicit <c>urn:payments</c> DTOs.
        /// </summary>
        public const string DataContractNamespace = "urn:payments";

        /// <summary>
        /// Fault action for typed payment faults.
        /// </summary>
        public const string FaultAction = OperationBase + "TransferPaymentFaultDetailFault";

        /// <summary>
        /// Service contract namespace.
        /// </summary>
        public const string Namespace = "urn:payments";

        /// <summary>
        /// Base URI prefix for payment operation actions.
        /// </summary>
        public const string OperationBase = Namespace + "/" + ContractName + "/";

        /// <summary>
        /// SOAP action for <c>Transfer</c>.
        /// </summary>
        public const string TransferAction = OperationBase + "Transfer";

        /// <summary>
        /// SOAP reply action for <c>Transfer</c>.
        /// </summary>
        public const string TransferReplyAction = OperationBase + "TransferResponse";

        #endregion
    }

    /// <summary>
    /// Payment status enumeration serialized across DCS and XmlSerializer fixtures.
    /// </summary>
    [DataContract(Namespace = PaymentConstants.DataContractNamespace)]
    public enum PaymentStatus
    {
        /// <summary>
        /// Payment transfer is pending processing.
        /// </summary>
        [EnumMember]
        Pending = 0,

        /// <summary>
        /// Payment transfer completed successfully.
        /// </summary>
        [EnumMember]
        Completed = 1,

        /// <summary>
        /// Payment transfer was rejected.
        /// </summary>
        [EnumMember]
        Rejected = 2
    }

    /// <summary>
    /// Default WCF <c>DataContract</c> request DTO (uses implicit CLR-derived namespace and alphabetical member ordering).
    /// </summary>
    [DataContract]
    public sealed class TransferRequestDefaultDcs
    {
        #region Properties

        /// <summary>
        /// Gets or sets the source account identifier.
        /// </summary>
        [DataMember]
        public string AccountNumber { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the monetary transfer amount.
        /// </summary>
        [DataMember]
        public decimal Amount { get; set; }

        /// <summary>
        /// Gets or sets the ISO currency code.
        /// </summary>
        [DataMember]
        public string Currency { get; set; } = "USD";

        /// <summary>
        /// Gets or sets an optional memo note (used to capture <c>i:nil="true"</c>).
        /// </summary>
        [DataMember]
        public string? Note { get; set; }

        #endregion
    }

    /// <summary>
    /// Default WCF <c>DataContract</c> result DTO (uses implicit CLR-derived namespace).
    /// </summary>
    [DataContract]
    public sealed class TransferResultDefaultDcs
    {
        #region Properties

        /// <summary>
        /// Gets or sets a value indicating whether the transfer succeeded.
        /// </summary>
        [DataMember]
        public bool Successful { get; set; }

        /// <summary>
        /// Gets or sets the assigned transaction identifier.
        /// </summary>
        [DataMember]
        public string TransactionId { get; set; } = string.Empty;

        #endregion
    }

    /// <summary>
    /// Nested metadata DTO in the <c>urn:payments</c> namespace for explicit DCS and XmlSerializer shapes.
    /// </summary>
    [DataContract(Namespace = PaymentConstants.DataContractNamespace)]
    public sealed class TransferMetadata
    {
        #region Properties

        /// <summary>
        /// Gets or sets the idempotency correlation key.
        /// </summary>
        [DataMember(Order = 0, IsRequired = true)]
        [XmlElement(Order = 0, Namespace = PaymentConstants.DataContractNamespace)]
        public string CorrelationId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets an optional originator reference (tests <c>nil</c> vs omission).
        /// </summary>
        [DataMember(Order = 1, EmitDefaultValue = false)]
        [XmlElement(Order = 1, Namespace = PaymentConstants.DataContractNamespace, IsNullable = true)]
        public string? Reference { get; set; }

        #endregion
    }

    /// <summary>
    /// Nested transfer request DTO explicitly placed in <c>urn:payments</c> with deterministic member order.
    /// </summary>
    [DataContract(Namespace = PaymentConstants.DataContractNamespace, Name = "TransferRequest")]
    public sealed class TransferRequest
    {
        #region Properties

        /// <summary>
        /// Gets or sets the source account identifier.
        /// </summary>
        [DataMember(Order = 0, IsRequired = true)]
        [XmlElement(Order = 0, Namespace = PaymentConstants.DataContractNamespace)]
        public string AccountNumber { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the transfer amount.
        /// </summary>
        [DataMember(Order = 1, IsRequired = true)]
        [XmlElement(Order = 1, Namespace = PaymentConstants.DataContractNamespace)]
        public decimal Amount { get; set; }

        /// <summary>
        /// Gets or sets the ISO-4217 currency code.
        /// </summary>
        [DataMember(Order = 2, IsRequired = true)]
        [XmlElement(Order = 2, Namespace = PaymentConstants.DataContractNamespace)]
        public string Currency { get; set; } = "USD";

        /// <summary>
        /// Gets or sets nested correlation metadata.
        /// </summary>
        [DataMember(Order = 3)]
        [XmlElement(Order = 3, Namespace = PaymentConstants.DataContractNamespace, IsNullable = true)]
        public TransferMetadata? Metadata { get; set; }

        /// <summary>
        /// Gets or sets an optional human-readable note.
        /// </summary>
        [DataMember(Order = 4)]
        [XmlElement(Order = 4, Namespace = PaymentConstants.DataContractNamespace, IsNullable = true)]
        public string? Note { get; set; }

        #endregion
    }

    /// <summary>
    /// Transfer result DTO explicitly placed in <c>urn:payments</c> with deterministic member order.
    /// </summary>
    [DataContract(Namespace = PaymentConstants.DataContractNamespace, Name = "TransferResult")]
    public sealed class TransferResult
    {
        #region Properties

        /// <summary>
        /// Gets or sets the UTC settlement timestamp.
        /// </summary>
        [DataMember(Order = 2)]
        [XmlElement(Order = 2, Namespace = PaymentConstants.DataContractNamespace)]
        public DateTime ProcessedAtUtc { get; set; }

        /// <summary>
        /// Gets or sets the resulting payment status enum.
        /// </summary>
        [DataMember(Order = 3)]
        [XmlElement(Order = 3, Namespace = PaymentConstants.DataContractNamespace)]
        public PaymentStatus Status { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the transfer succeeded.
        /// </summary>
        [DataMember(Order = 1)]
        [XmlElement(Order = 1, Namespace = PaymentConstants.DataContractNamespace)]
        public bool Successful { get; set; }

        /// <summary>
        /// Gets or sets the assigned transaction identifier.
        /// </summary>
        [DataMember(Order = 0)]
        [XmlElement(Order = 0, Namespace = PaymentConstants.DataContractNamespace)]
        public string TransactionId { get; set; } = string.Empty;

        #endregion
    }

    /// <summary>
    /// Typed fault detail contract for <c>urn:payments</c>, mapped to CoreWCF <c>FaultContractTests</c>.
    /// </summary>
    [DataContract(Namespace = PaymentConstants.DataContractNamespace)]
    public sealed class PaymentFaultDetail
    {
        #region Properties

        /// <summary>
        /// Gets or sets the machine-readable error code.
        /// </summary>
        [DataMember(Order = 0)]
        public string ErrorCode { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the safe diagnostic message.
        /// </summary>
        [DataMember(Order = 1)]
        public string Message { get; set; } = string.Empty;

        #endregion
    }

    /// <summary>
    /// Collection-bearing batch transfer request DTO in <c>urn:payments</c>.
    /// </summary>
    [DataContract(Namespace = PaymentConstants.DataContractNamespace)]
    public sealed class BatchTransferRequest
    {
        #region Properties

        /// <summary>
        /// Gets or sets the batch identifier.
        /// </summary>
        [DataMember(Order = 0)]
        [XmlElement(Order = 0, Namespace = PaymentConstants.DataContractNamespace)]
        public string BatchId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the ordered list of transfer requests in this batch.
        /// </summary>
        [DataMember(Order = 1)]
        [XmlArray(Order = 1, Namespace = PaymentConstants.DataContractNamespace)]
        [XmlArrayItem(ElementName = "TransferRequest", Namespace = PaymentConstants.DataContractNamespace)]
        public List<TransferRequest> Items { get; set; } = [];

        #endregion
    }

    /// <summary>
    /// Collection-bearing batch transfer result DTO in <c>urn:payments</c>.
    /// </summary>
    [DataContract(Namespace = PaymentConstants.DataContractNamespace)]
    public sealed class BatchTransferResult
    {
        #region Properties

        /// <summary>
        /// Gets or sets the batch identifier.
        /// </summary>
        [DataMember(Order = 0)]
        [XmlElement(Order = 0, Namespace = PaymentConstants.DataContractNamespace)]
        public string BatchId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the ordered list of individual transfer results.
        /// </summary>
        [DataMember(Order = 1)]
        [XmlArray(Order = 1, Namespace = PaymentConstants.DataContractNamespace)]
        [XmlArrayItem(ElementName = "TransferResult", Namespace = PaymentConstants.DataContractNamespace)]
        public List<TransferResult> Results { get; set; } = [];

        #endregion
    }

    /// <summary>
    /// Server-side CoreWCF contract using default DataContractSerializer namespaces.
    /// </summary>
    [CoreWCF.ServiceContract(Namespace = PaymentConstants.Namespace, Name = PaymentConstants.ContractName)]
    public interface IPaymentDcsDefaultService
    {
        #region Public Methods

        /// <summary>
        /// Executes a transfer using default WCF DataContract namespace conventions.
        /// </summary>
        /// <param name="request">The transfer request.</param>
        /// <returns>The transfer result.</returns>
        [CoreWCF.OperationContract(
            Name = "Transfer",
            Action = PaymentConstants.TransferAction,
            ReplyAction = PaymentConstants.TransferReplyAction)]
        Task<TransferResultDefaultDcs> TransferAsync(TransferRequestDefaultDcs request);

        #endregion
    }

    /// <summary>
    /// Client-side WCF contract using default DataContractSerializer namespaces.
    /// </summary>
    [ServiceContract(Namespace = PaymentConstants.Namespace, Name = PaymentConstants.ContractName)]
    public interface IPaymentDcsDefaultClient
    {
        #region Public Methods

        /// <summary>
        /// Executes a transfer using default WCF DataContract namespace conventions.
        /// </summary>
        /// <param name="request">The transfer request.</param>
        /// <returns>The transfer result.</returns>
        [OperationContract(
            Name = "Transfer",
            Action = PaymentConstants.TransferAction,
            ReplyAction = PaymentConstants.TransferReplyAction)]
        Task<TransferResultDefaultDcs> TransferAsync(TransferRequestDefaultDcs request);

        #endregion
    }

    /// <summary>
    /// Server-side CoreWCF contract using explicit <c>urn:payments</c> DataContract namespaces, collections, and typed faults.
    /// </summary>
    [CoreWCF.ServiceContract(Namespace = PaymentConstants.Namespace, Name = PaymentConstants.ContractName)]
    public interface IPaymentDcsUrnService
    {
        #region Public Methods

        /// <summary>
        /// Executes a batch of transfers.
        /// </summary>
        /// <param name="request">The batch transfer request.</param>
        /// <returns>The batch transfer result.</returns>
        [CoreWCF.OperationContract(
            Name = "BatchTransfer",
            Action = PaymentConstants.BatchTransferAction,
            ReplyAction = PaymentConstants.BatchTransferReplyAction)]
        Task<BatchTransferResult> BatchTransferAsync(BatchTransferRequest request);

        /// <summary>
        /// Executes a single nested transfer, raising typed <see cref="PaymentFaultDetail"/> on invalid input.
        /// </summary>
        /// <param name="request">The transfer request.</param>
        /// <returns>The transfer result.</returns>
        [CoreWCF.OperationContract(
            Name = "Transfer",
            Action = PaymentConstants.TransferAction,
            ReplyAction = PaymentConstants.TransferReplyAction)]
        [CoreWCF.FaultContract(typeof(PaymentFaultDetail), Action = PaymentConstants.FaultAction)]
        Task<TransferResult> TransferAsync(TransferRequest request);

        #endregion
    }

    /// <summary>
    /// Client-side WCF contract using explicit <c>urn:payments</c> DataContract namespaces, collections, and typed faults.
    /// </summary>
    [ServiceContract(Namespace = PaymentConstants.Namespace, Name = PaymentConstants.ContractName)]
    public interface IPaymentDcsUrnClient
    {
        #region Public Methods

        /// <summary>
        /// Executes a batch of transfers.
        /// </summary>
        /// <param name="request">The batch transfer request.</param>
        /// <returns>The batch transfer result.</returns>
        [OperationContract(
            Name = "BatchTransfer",
            Action = PaymentConstants.BatchTransferAction,
            ReplyAction = PaymentConstants.BatchTransferReplyAction)]
        Task<BatchTransferResult> BatchTransferAsync(BatchTransferRequest request);

        /// <summary>
        /// Executes a single nested transfer, raising typed <see cref="PaymentFaultDetail"/> on invalid input.
        /// </summary>
        /// <param name="request">The transfer request.</param>
        /// <returns>The transfer result.</returns>
        [OperationContract(
            Name = "Transfer",
            Action = PaymentConstants.TransferAction,
            ReplyAction = PaymentConstants.TransferReplyAction)]
        [FaultContract(typeof(PaymentFaultDetail), Action = PaymentConstants.FaultAction)]
        Task<TransferResult> TransferAsync(TransferRequest request);

        #endregion
    }

    /// <summary>
    /// Server-side CoreWCF contract using <c>XmlSerializerFormat</c> in <c>urn:payments</c>.
    /// </summary>
    [CoreWCF.XmlSerializerFormat]
    [CoreWCF.ServiceContract(Namespace = PaymentConstants.Namespace, Name = PaymentConstants.ContractName)]
    public interface IPaymentXmlSerializerService
    {
        #region Public Methods

        /// <summary>
        /// Executes a single nested transfer using <c>XmlSerializer</c>.
        /// </summary>
        /// <param name="request">The transfer request.</param>
        /// <returns>The transfer result.</returns>
        [CoreWCF.OperationContract(
            Name = "Transfer",
            Action = PaymentConstants.TransferAction,
            ReplyAction = PaymentConstants.TransferReplyAction)]
        Task<TransferResult> TransferAsync(TransferRequest request);

        #endregion
    }

    /// <summary>
    /// Client-side WCF contract using <c>XmlSerializerFormat</c> in <c>urn:payments</c>.
    /// </summary>
    [XmlSerializerFormat]
    [ServiceContract(Namespace = PaymentConstants.Namespace, Name = PaymentConstants.ContractName)]
    public interface IPaymentXmlSerializerClient
    {
        #region Public Methods

        /// <summary>
        /// Executes a single nested transfer using <c>XmlSerializer</c>.
        /// </summary>
        /// <param name="request">The transfer request.</param>
        /// <returns>The transfer result.</returns>
        [OperationContract(
            Name = "Transfer",
            Action = PaymentConstants.TransferAction,
            ReplyAction = PaymentConstants.TransferReplyAction)]
        Task<TransferResult> TransferAsync(TransferRequest request);

        #endregion
    }
}
