using System;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

using CloudNimble.Text.Xml.Serialization;

using FastSoap.Abstractions;

namespace FastSoap.Benchmarks.Contracts
{

    /// <summary>
    /// Payment status enumeration shared across FastSoap, CoreWCF, and SoapCore benchmark contracts.
    /// </summary>
    [DataContract(Namespace = BenchmarkConstants.PaymentsNamespace)]
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
    /// Shared SOAP 1.1 contract constants matching the canonical R0-G Echo and Transfer wire shapes.
    /// </summary>
    public static class BenchmarkConstants
    {

        #region Fields

        /// <summary>
        /// SOAP 1.1 action for <c>EchoString</c>.
        /// </summary>
        public const string EchoAction = "http://tempuri.org/IEchoService/EchoString";

        /// <summary>
        /// Echo service contract name.
        /// </summary>
        public const string EchoContractName = "IEchoService";

        /// <summary>
        /// Echo service XML namespace.
        /// </summary>
        public const string EchoNamespace = "http://tempuri.org/";

        /// <summary>
        /// SOAP 1.1 reply action for <c>EchoString</c>.
        /// </summary>
        public const string EchoReplyAction = "http://tempuri.org/IEchoService/EchoStringResponse";

        /// <summary>
        /// Payment service contract name.
        /// </summary>
        public const string PaymentsContractName = "IPaymentService";

        /// <summary>
        /// Payment service XML namespace.
        /// </summary>
        public const string PaymentsNamespace = "urn:payments";

        /// <summary>
        /// SOAP 1.1 action for <c>Transfer</c>.
        /// </summary>
        public const string TransferAction = "urn:payments/IPaymentService/Transfer";

        /// <summary>
        /// SOAP 1.1 reply action for <c>Transfer</c>.
        /// </summary>
        public const string TransferReplyAction = "urn:payments/IPaymentService/TransferResponse";

        #endregion

    }

    /// <summary>
    /// FastSoap contract-first Echo service interface processed by <c>FastSoap.Generators</c>.
    /// </summary>
    [SoapService(Name = "BenchmarkEchoService", Namespace = BenchmarkConstants.EchoNamespace)]
    [SoapWireContract(typeof(BenchmarkXmlContext))]
    public interface IBenchmarkEchoService
    {

        #region Public Methods

        /// <summary>
        /// Echoes the supplied string payload back to the caller.
        /// </summary>
        /// <param name="request">The wrapped Echo request DTO.</param>
        /// <param name="cancellationToken">A cancellation token tied to the request.</param>
        /// <returns>The wrapped Echo response DTO.</returns>
        [SoapOperation(
            Name = "EchoString",
            Action = BenchmarkConstants.EchoAction,
            ReplyAction = BenchmarkConstants.EchoReplyAction)]
        [SoapWireContract(
            RequestType = typeof(EchoStringRequestWire),
            ResponseType = typeof(EchoStringResponseWire))]
        Task<EchoStringResponseWire> EchoStringAsync(EchoStringRequestWire request, CancellationToken cancellationToken);

        #endregion

    }

    /// <summary>
    /// FastSoap contract-first nested payment service interface processed by <c>FastSoap.Generators</c>.
    /// </summary>
    [SoapService(Name = "BenchmarkPaymentService", Namespace = BenchmarkConstants.PaymentsNamespace)]
    [SoapWireContract(typeof(BenchmarkXmlContext))]
    public interface IBenchmarkPaymentService
    {

        #region Public Methods

        /// <summary>
        /// Executes a nested payment transfer request.
        /// </summary>
        /// <param name="request">The nested transfer request DTO.</param>
        /// <param name="cancellationToken">A cancellation token tied to the request.</param>
        /// <returns>The transfer result DTO.</returns>
        [SoapOperation(
            Name = "Transfer",
            Action = BenchmarkConstants.TransferAction,
            ReplyAction = BenchmarkConstants.TransferReplyAction)]
        [SoapWireContract(
            RequestType = typeof(TransferRequestWire),
            RequestMemberName = nameof(TransferRequestWire.Request),
            ResponseType = typeof(TransferResponseWire),
            ResponseMemberName = nameof(TransferResponseWire.TransferResult))]
        Task<TransferResult> TransferAsync(TransferRequest request, CancellationToken cancellationToken);

        #endregion

    }

    /// <summary>
    /// CoreWCF <c>DataContractSerializer</c> Echo service contract.
    /// </summary>
    [CoreWCF.ServiceContract(Namespace = BenchmarkConstants.EchoNamespace, Name = BenchmarkConstants.EchoContractName)]
    public interface ICoreWcfEchoDcsService
    {

        #region Public Methods

        /// <summary>
        /// Echoes the provided string payload back to the caller.
        /// </summary>
        /// <param name="echo">The string to echo.</param>
        /// <returns>The echoed string.</returns>
        [CoreWCF.OperationContract(
            Name = "EchoString",
            Action = BenchmarkConstants.EchoAction,
            ReplyAction = BenchmarkConstants.EchoReplyAction)]
        Task<string?> EchoStringAsync(string? echo);

        #endregion

    }

    /// <summary>
    /// CoreWCF <c>XmlSerializerFormat</c> Echo service contract.
    /// </summary>
    [CoreWCF.XmlSerializerFormat]
    [CoreWCF.ServiceContract(Namespace = BenchmarkConstants.EchoNamespace, Name = BenchmarkConstants.EchoContractName)]
    public interface ICoreWcfEchoXmlService
    {

        #region Public Methods

        /// <summary>
        /// Echoes the provided string payload back to the caller.
        /// </summary>
        /// <param name="echo">The string to echo.</param>
        /// <returns>The echoed string.</returns>
        [CoreWCF.OperationContract(
            Name = "EchoString",
            Action = BenchmarkConstants.EchoAction,
            ReplyAction = BenchmarkConstants.EchoReplyAction)]
        Task<string?> EchoStringAsync(string? echo);

        #endregion

    }

    /// <summary>
    /// CoreWCF <c>DataContractSerializer</c> nested payment service contract.
    /// </summary>
    [CoreWCF.ServiceContract(Namespace = BenchmarkConstants.PaymentsNamespace, Name = BenchmarkConstants.PaymentsContractName)]
    public interface ICoreWcfPaymentDcsService
    {

        #region Public Methods

        /// <summary>
        /// Executes a nested payment transfer request.
        /// </summary>
        /// <param name="request">The nested transfer request.</param>
        /// <returns>The transfer result.</returns>
        [CoreWCF.OperationContract(
            Name = "Transfer",
            Action = BenchmarkConstants.TransferAction,
            ReplyAction = BenchmarkConstants.TransferReplyAction)]
        Task<TransferResult> TransferAsync(TransferRequest request);

        #endregion

    }

    /// <summary>
    /// CoreWCF <c>XmlSerializerFormat</c> nested payment service contract.
    /// </summary>
    [CoreWCF.XmlSerializerFormat]
    [CoreWCF.ServiceContract(Namespace = BenchmarkConstants.PaymentsNamespace, Name = BenchmarkConstants.PaymentsContractName)]
    public interface ICoreWcfPaymentXmlService
    {

        #region Public Methods

        /// <summary>
        /// Executes a nested payment transfer request.
        /// </summary>
        /// <param name="request">The nested transfer request.</param>
        /// <returns>The transfer result.</returns>
        [CoreWCF.OperationContract(
            Name = "Transfer",
            Action = BenchmarkConstants.TransferAction,
            ReplyAction = BenchmarkConstants.TransferReplyAction)]
        Task<TransferResult> TransferAsync(TransferRequest request);

        #endregion

    }

    /// <summary>
    /// SoapCore <c>XmlSerializer</c> Echo service contract matching the canonical <c>EchoString</c> wire shape.
    /// </summary>
    [System.ServiceModel.XmlSerializerFormat]
    [System.ServiceModel.ServiceContract(Namespace = BenchmarkConstants.EchoNamespace, Name = BenchmarkConstants.EchoContractName)]
    public interface ISoapCoreEchoService
    {

        #region Public Methods

        /// <summary>
        /// Echoes the provided string payload back to the caller.
        /// </summary>
        /// <param name="echo">The string to echo.</param>
        /// <returns>The echoed string.</returns>
        [System.ServiceModel.OperationContract(
            Name = "EchoString",
            Action = BenchmarkConstants.EchoAction,
            ReplyAction = BenchmarkConstants.EchoReplyAction)]
        Task<string?> EchoStringAsync(string? echo);

        #endregion

    }

    /// <summary>
    /// SoapCore <c>XmlSerializer</c> nested payment service contract matching the canonical <c>urn:payments</c> wire shape.
    /// </summary>
    [System.ServiceModel.XmlSerializerFormat]
    [System.ServiceModel.ServiceContract(Namespace = BenchmarkConstants.PaymentsNamespace, Name = BenchmarkConstants.PaymentsContractName)]
    public interface ISoapCorePaymentService
    {

        #region Public Methods

        /// <summary>
        /// Executes a nested payment transfer request.
        /// </summary>
        /// <param name="request">The nested transfer request.</param>
        /// <returns>The transfer result.</returns>
        [System.ServiceModel.OperationContract(
            Name = "Transfer",
            Action = BenchmarkConstants.TransferAction,
            ReplyAction = BenchmarkConstants.TransferReplyAction)]
        Task<TransferResult> TransferAsync(TransferRequest request);

        #endregion

    }

    /// <summary>
    /// Source-generated <c>XmlEssentials</c> serializer context for FastSoap benchmark wire DTOs.
    /// </summary>
    [XmlSerializable<EchoStringRequestWire>]
    [XmlSerializable<EchoStringResponseWire>]
    [XmlSerializable<TransferRequestWire>]
    [XmlSerializable<TransferResponseWire>]
    public partial class BenchmarkXmlContext : XmlSerializerContext
    {
    }

    /// <summary>
    /// Document/literal wrapped <c>EchoString</c> request DTO.
    /// </summary>
    [XmlRoot(ElementName = "EchoString", Namespace = BenchmarkConstants.EchoNamespace)]
    public sealed class EchoStringRequestWire
    {

        #region Properties

        /// <summary>
        /// Gets or sets the input string to echo.
        /// </summary>
        [XmlElement(ElementName = "echo", Namespace = BenchmarkConstants.EchoNamespace, IsNullable = true)]
        public string? Echo { get; set; }

        #endregion

    }

    /// <summary>
    /// Document/literal wrapped <c>EchoStringResponse</c> DTO.
    /// </summary>
    [XmlRoot(ElementName = "EchoStringResponse", Namespace = BenchmarkConstants.EchoNamespace)]
    public sealed class EchoStringResponseWire
    {

        #region Properties

        /// <summary>
        /// Gets or sets the echoed result string.
        /// </summary>
        [XmlElement(ElementName = "EchoStringResult", Namespace = BenchmarkConstants.EchoNamespace, IsNullable = true)]
        public string? EchoStringResult { get; set; }

        #endregion

    }

    /// <summary>
    /// Nested correlation metadata DTO shared across FastSoap, CoreWCF, and SoapCore.
    /// </summary>
    [DataContract(Namespace = BenchmarkConstants.PaymentsNamespace)]
    public sealed class TransferMetadata
    {

        #region Properties

        /// <summary>
        /// Gets or sets the idempotency correlation key.
        /// </summary>
        [DataMember(Order = 0, IsRequired = true)]
        [System.Xml.Serialization.XmlElement(Order = 0, Namespace = BenchmarkConstants.PaymentsNamespace)]
        [XmlElement(ElementName = "CorrelationId", Namespace = BenchmarkConstants.PaymentsNamespace, Order = 0)]
        public string CorrelationId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets an optional originator reference.
        /// </summary>
        [DataMember(Order = 1, EmitDefaultValue = false)]
        [System.Xml.Serialization.XmlElement(Order = 1, Namespace = BenchmarkConstants.PaymentsNamespace, IsNullable = true)]
        [XmlElement(ElementName = "Reference", Namespace = BenchmarkConstants.PaymentsNamespace, IsNullable = true, Order = 1)]
        public string? Reference { get; set; }

        #endregion

    }

    /// <summary>
    /// Nested payment transfer request DTO shared across FastSoap, CoreWCF, and SoapCore.
    /// </summary>
    [DataContract(Namespace = BenchmarkConstants.PaymentsNamespace, Name = "TransferRequest")]
    [System.Xml.Serialization.XmlRoot("request", Namespace = BenchmarkConstants.PaymentsNamespace)]
    public sealed class TransferRequest
    {

        #region Properties

        /// <summary>
        /// Gets or sets the source account identifier.
        /// </summary>
        [DataMember(Order = 0, IsRequired = true)]
        [System.Xml.Serialization.XmlElement(Order = 0, Namespace = BenchmarkConstants.PaymentsNamespace)]
        [XmlElement(ElementName = "AccountNumber", Namespace = BenchmarkConstants.PaymentsNamespace, Order = 0)]
        public string AccountNumber { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the monetary transfer amount.
        /// </summary>
        [DataMember(Order = 1, IsRequired = true)]
        [System.Xml.Serialization.XmlElement(Order = 1, Namespace = BenchmarkConstants.PaymentsNamespace)]
        [XmlElement(ElementName = "Amount", Namespace = BenchmarkConstants.PaymentsNamespace, Order = 1)]
        public decimal Amount { get; set; }

        /// <summary>
        /// Gets or sets the ISO-4217 currency code.
        /// </summary>
        [DataMember(Order = 2, IsRequired = true)]
        [System.Xml.Serialization.XmlElement(Order = 2, Namespace = BenchmarkConstants.PaymentsNamespace)]
        [XmlElement(ElementName = "Currency", Namespace = BenchmarkConstants.PaymentsNamespace, Order = 2)]
        public string Currency { get; set; } = "USD";

        /// <summary>
        /// Gets or sets optional correlation metadata.
        /// </summary>
        [DataMember(Order = 3)]
        [System.Xml.Serialization.XmlElement(Order = 3, Namespace = BenchmarkConstants.PaymentsNamespace, IsNullable = true)]
        [XmlElement(ElementName = "Metadata", Namespace = BenchmarkConstants.PaymentsNamespace, IsNullable = true, Order = 3)]
        public TransferMetadata? Metadata { get; set; }

        /// <summary>
        /// Gets or sets an optional memo note.
        /// </summary>
        [DataMember(Order = 4)]
        [System.Xml.Serialization.XmlElement(Order = 4, Namespace = BenchmarkConstants.PaymentsNamespace, IsNullable = true)]
        [XmlElement(ElementName = "Note", Namespace = BenchmarkConstants.PaymentsNamespace, IsNullable = true, Order = 4)]
        public string? Note { get; set; }

        #endregion

    }

    /// <summary>
    /// Document/literal wrapped <c>Transfer</c> request wire element for FastSoap.
    /// </summary>
    [XmlRoot(ElementName = "Transfer", Namespace = BenchmarkConstants.PaymentsNamespace)]
    public sealed class TransferRequestWire
    {

        #region Properties

        /// <summary>
        /// Gets or sets the nested transfer request parameter.
        /// </summary>
        [XmlElement(ElementName = "request", Namespace = BenchmarkConstants.PaymentsNamespace)]
        public required TransferRequest Request { get; set; }

        #endregion

    }

    /// <summary>
    /// Document/literal wrapped <c>TransferResponse</c> wire element for FastSoap.
    /// </summary>
    [XmlRoot(ElementName = "TransferResponse", Namespace = BenchmarkConstants.PaymentsNamespace)]
    public sealed class TransferResponseWire
    {

        #region Properties

        /// <summary>
        /// Gets or sets the nested transfer result.
        /// </summary>
        [XmlElement(ElementName = "TransferResult", Namespace = BenchmarkConstants.PaymentsNamespace)]
        public required TransferResult TransferResult { get; set; }

        #endregion

    }

    /// <summary>
    /// Nested payment transfer result DTO shared across FastSoap, CoreWCF, and SoapCore.
    /// </summary>
    [DataContract(Namespace = BenchmarkConstants.PaymentsNamespace, Name = "TransferResult")]
    [System.Xml.Serialization.XmlRoot("TransferResult", Namespace = BenchmarkConstants.PaymentsNamespace)]
    public sealed class TransferResult
    {

        #region Properties

        /// <summary>
        /// Gets or sets the UTC settlement timestamp.
        /// </summary>
        [DataMember(Order = 2)]
        [System.Xml.Serialization.XmlElement(Order = 2, Namespace = BenchmarkConstants.PaymentsNamespace)]
        [XmlElement(ElementName = "ProcessedAtUtc", Namespace = BenchmarkConstants.PaymentsNamespace, Order = 2)]
        public DateTime ProcessedAtUtc { get; set; }

        /// <summary>
        /// Gets or sets the resulting payment status enum.
        /// </summary>
        [DataMember(Order = 3)]
        [System.Xml.Serialization.XmlElement(Order = 3, Namespace = BenchmarkConstants.PaymentsNamespace)]
        [XmlElement(ElementName = "Status", Namespace = BenchmarkConstants.PaymentsNamespace, Order = 3)]
        public PaymentStatus Status { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the transfer succeeded.
        /// </summary>
        [DataMember(Order = 1)]
        [System.Xml.Serialization.XmlElement(Order = 1, Namespace = BenchmarkConstants.PaymentsNamespace)]
        [XmlElement(ElementName = "Successful", Namespace = BenchmarkConstants.PaymentsNamespace, Order = 1)]
        public bool Successful { get; set; }

        /// <summary>
        /// Gets or sets the assigned transaction identifier.
        /// </summary>
        [DataMember(Order = 0)]
        [System.Xml.Serialization.XmlElement(Order = 0, Namespace = BenchmarkConstants.PaymentsNamespace)]
        [XmlElement(ElementName = "TransactionId", Namespace = BenchmarkConstants.PaymentsNamespace, Order = 0)]
        public string TransactionId { get; set; } = string.Empty;

        #endregion

    }

}
