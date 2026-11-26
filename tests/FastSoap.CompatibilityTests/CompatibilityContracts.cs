using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;

using CloudNimble.Text.Xml.Serialization;

using FastSoap.Abstractions;

namespace FastSoap.CompatibilityTests
{

    [DataContract(Namespace = "urn:payments")]
    public enum PaymentStatus
    {
        [EnumMember]
        Pending = 0,

        [EnumMember]
        Completed = 1,

        [EnumMember]
        Rejected = 2
    }

    [ServiceContract(Namespace = "http://tempuri.org/", Name = "IEchoService")]
    public interface IWcfEchoClient
    {

        #region Public Methods

        [OperationContract(
            Name = "EchoString",
            Action = "http://tempuri.org/IEchoService/EchoString",
            ReplyAction = "http://tempuri.org/IEchoService/EchoStringResponse")]
        Task<string?> EchoStringAsync(string? echo);

        #endregion

    }

    [ServiceContract(Namespace = "urn:payments", Name = "IPaymentService")]
    public interface IWcfPaymentClient
    {

        #region Public Methods

        [OperationContract(
            Name = "BatchTransfer",
            Action = "urn:payments/IPaymentService/BatchTransfer",
            ReplyAction = "urn:payments/IPaymentService/BatchTransferResponse")]
        Task<BatchTransferResult> BatchTransferAsync(BatchTransferRequest request);

        [OperationContract(
            Name = "Transfer",
            Action = "urn:payments/IPaymentService/Transfer",
            ReplyAction = "urn:payments/IPaymentService/TransferResponse")]
        [FaultContract(typeof(PaymentFaultDetail), Action = "urn:payments/IPaymentService/TransferPaymentFaultDetailFault")]
        Task<TransferResult> TransferAsync(TransferRequest request);

        #endregion

    }

    [DataContract(Namespace = "urn:payments")]
    public sealed class BatchTransferRequest
    {

        #region Properties

        [DataMember(Order = 0)]
        [XmlElement(ElementName = "BatchId", Namespace = "urn:payments", Order = 0)]
        public string BatchId { get; set; } = string.Empty;

        [DataMember(Order = 1)]
        [XmlArray(ElementName = "Items", Namespace = "urn:payments", Order = 1)]
        [XmlArrayItem(ElementName = "TransferRequest", Namespace = "urn:payments")]
        public List<TransferRequest> Items { get; set; } = [];

        #endregion

    }

    [XmlRoot(ElementName = "BatchTransfer", Namespace = "urn:payments")]
    public sealed class BatchTransferRequestWire
    {

        #region Properties

        [XmlElement(ElementName = "request", Namespace = "urn:payments")]
        public required BatchTransferRequest Request { get; set; }

        #endregion

    }

    [XmlRoot(ElementName = "BatchTransferResponse", Namespace = "urn:payments")]
    public sealed class BatchTransferResponseWire
    {

        #region Properties

        [XmlElement(ElementName = "BatchTransferResult", Namespace = "urn:payments")]
        public required BatchTransferResult BatchTransferResult { get; set; }

        #endregion

    }

    [DataContract(Namespace = "urn:payments")]
    public sealed class BatchTransferResult
    {

        #region Properties

        [DataMember(Order = 0)]
        [XmlElement(ElementName = "BatchId", Namespace = "urn:payments", Order = 0)]
        public string BatchId { get; set; } = string.Empty;

        [DataMember(Order = 1)]
        [XmlArray(ElementName = "Results", Namespace = "urn:payments", Order = 1)]
        [XmlArrayItem(ElementName = "TransferResult", Namespace = "urn:payments")]
        public List<TransferResult> Results { get; set; } = [];

        #endregion

    }

    [XmlSerializable<BatchTransferRequestWire>]
    [XmlSerializable<BatchTransferResponseWire>]
    [XmlSerializable<DcsEchoStringPayload>]
    [XmlSerializable<EchoHeaderRequestWire>]
    [XmlSerializable<EchoHeaderResponseWire>]
    [XmlSerializable<EchoStringRequestWire>]
    [XmlSerializable<EchoStringResponseWire>]
    [XmlSerializable<TransferRequest>]
    [XmlSerializable<TransferRequestWire>]
    [XmlSerializable<TransferResponseWire>]
    [XmlSerializable<TransferResult>]
    [XmlSerializable<BatchTransferRequest>]
    [XmlSerializable<XmlSerEchoStringPayload>]
    public partial class CompatibilityXmlContext : XmlSerializerContext
    {
    }

    [XmlRoot(ElementName = "string", Namespace = "http://schemas.microsoft.com/2003/10/Serialization/")]
    public sealed class DcsEchoStringPayload
    {

        #region Properties

        [XmlText]
        public string Value { get; set; } = string.Empty;

        #endregion

    }

    [XmlRoot(ElementName = "string", Namespace = "")]
    public sealed class XmlSerEchoStringPayload
    {

        #region Properties

        [XmlText]
        public string Value { get; set; } = string.Empty;

        #endregion

    }

    [XmlRoot(ElementName = "EchoHeaderRequest", Namespace = "http://tempuri.org/")]
    public sealed class EchoHeaderRequestWire
    {

        #region Properties

        [XmlElement(ElementName = "Text", Namespace = "http://tempuri.org/")]
        public string? Text { get; set; }

        #endregion

    }

    [XmlRoot(ElementName = "EchoHeaderResponse", Namespace = "http://tempuri.org/")]
    public sealed class EchoHeaderResponseWire
    {

        #region Properties

        [XmlElement(ElementName = "SayHello", Namespace = "http://tempuri.org/")]
        public string? SayHello { get; set; }

        #endregion

    }

    [XmlRoot(ElementName = "EchoString", Namespace = "http://tempuri.org/")]
    public sealed class EchoStringRequestWire
    {

        #region Properties

        [XmlElement(ElementName = "echo", Namespace = "http://tempuri.org/", IsNullable = true)]
        public string? Echo { get; set; }

        #endregion

    }

    [XmlRoot(ElementName = "EchoStringResponse", Namespace = "http://tempuri.org/")]
    public sealed class EchoStringResponseWire
    {

        #region Properties

        [XmlElement(ElementName = "EchoStringResult", Namespace = "http://tempuri.org/", IsNullable = true)]
        public string? EchoStringResult { get; set; }

        #endregion

    }

    [DataContract(Namespace = "urn:payments")]
    public sealed class PaymentFaultDetail
    {

        #region Properties

        [DataMember(Order = 0)]
        public string ErrorCode { get; set; } = string.Empty;

        [DataMember(Order = 1)]
        public string Message { get; set; } = string.Empty;

        #endregion

    }

    [DataContract(Namespace = "urn:payments")]
    public sealed class TransferMetadata
    {

        #region Properties

        [DataMember(Order = 0, IsRequired = true)]
        [XmlElement(ElementName = "CorrelationId", Namespace = "urn:payments", Order = 0)]
        public string CorrelationId { get; set; } = string.Empty;

        [DataMember(Order = 1, EmitDefaultValue = false)]
        [XmlElement(ElementName = "Reference", Namespace = "urn:payments", IsNullable = true, Order = 1)]
        public string? Reference { get; set; }

        #endregion

    }

    [DataContract(Namespace = "urn:payments", Name = "TransferRequest")]
    public sealed class TransferRequest
    {

        #region Properties

        [DataMember(Order = 0, IsRequired = true)]
        [XmlElement(ElementName = "AccountNumber", Namespace = "urn:payments", Order = 0)]
        public string AccountNumber { get; set; } = string.Empty;

        [DataMember(Order = 1, IsRequired = true)]
        [XmlElement(ElementName = "Amount", Namespace = "urn:payments", Order = 1)]
        public decimal Amount { get; set; }

        [DataMember(Order = 2, IsRequired = true)]
        [XmlElement(ElementName = "Currency", Namespace = "urn:payments", Order = 2)]
        public string Currency { get; set; } = "USD";

        [DataMember(Order = 3)]
        [XmlElement(ElementName = "Metadata", Namespace = "urn:payments", IsNullable = true, Order = 3)]
        public TransferMetadata? Metadata { get; set; }

        [DataMember(Order = 4)]
        [XmlElement(ElementName = "Note", Namespace = "urn:payments", IsNullable = true, Order = 4)]
        public string? Note { get; set; }

        #endregion

    }

    [XmlRoot(ElementName = "Transfer", Namespace = "urn:payments")]
    public sealed class TransferRequestWire
    {

        #region Properties

        [XmlElement(ElementName = "request", Namespace = "urn:payments")]
        public required TransferRequest Request { get; set; }

        #endregion

    }

    [XmlRoot(ElementName = "TransferResponse", Namespace = "urn:payments")]
    public sealed class TransferResponseWire
    {

        #region Properties

        [XmlElement(ElementName = "TransferResult", Namespace = "urn:payments")]
        public required TransferResult TransferResult { get; set; }

        #endregion

    }

    [DataContract(Namespace = "urn:payments", Name = "TransferResult")]
    public sealed class TransferResult
    {

        #region Properties

        [DataMember(Order = 2)]
        [XmlElement(ElementName = "ProcessedAtUtc", Namespace = "urn:payments", Order = 2)]
        public DateTime ProcessedAtUtc { get; set; }

        [DataMember(Order = 3)]
        [XmlElement(ElementName = "Status", Namespace = "urn:payments", Order = 3)]
        public PaymentStatus Status { get; set; }

        [DataMember(Order = 1)]
        [XmlElement(ElementName = "Successful", Namespace = "urn:payments", Order = 1)]
        public bool Successful { get; set; }

        [DataMember(Order = 0)]
        [XmlElement(ElementName = "TransactionId", Namespace = "urn:payments", Order = 0)]
        public string TransactionId { get; set; } = string.Empty;

        #endregion

    }

    internal static class CompatibilityDescriptors
    {

        #region Properties

        internal static SoapOperationDescriptor BatchTransfer { get; } = new SoapOperationDescriptor(
            "BatchTransfer",
            "urn:payments/IPaymentService/BatchTransfer",
            "urn:payments/IPaymentService/BatchTransferResponse",
            new SoapQName("urn:payments", "BatchTransfer"),
            new SoapQName("urn:payments", "BatchTransferResponse"));

        internal static SoapOperationDescriptor EchoHeader { get; } = new SoapOperationDescriptor(
            "EchoWithMessageContract",
            "http://tempuri.org/IServiceWithMessageBodyAndHeader/EchoWithMessageContract",
            "http://tempuri.org/IServiceWithMessageBodyAndHeader/EchoWithMessageContractResponse",
            new SoapQName("http://tempuri.org/", "EchoHeaderRequest"),
            new SoapQName("http://tempuri.org/", "EchoHeaderResponse"));

        internal static SoapOperationDescriptor EchoString { get; } = new SoapOperationDescriptor(
            "EchoString",
            "http://tempuri.org/IEchoService/EchoString",
            "http://tempuri.org/IEchoService/EchoStringResponse",
            new SoapQName("http://tempuri.org/", "EchoString"),
            new SoapQName("http://tempuri.org/", "EchoStringResponse"));

        internal static SoapOperationDescriptor Transfer { get; } = new SoapOperationDescriptor(
            "Transfer",
            "urn:payments/IPaymentService/Transfer",
            "urn:payments/IPaymentService/TransferResponse",
            new SoapQName("urn:payments", "Transfer"),
            new SoapQName("urn:payments", "TransferResponse"));

        #endregion

    }

    internal sealed class RecordingHeaderHandler : ISoapHeaderHandler
    {

        #region Fields

        private readonly List<SoapHeaderValue> _headers = new List<SoapHeaderValue>();

        #endregion

        #region Properties

        public SoapQName HeaderName { get; }

        public IReadOnlyList<SoapHeaderValue> Headers => _headers;

        #endregion

        #region Constructors

        public RecordingHeaderHandler(SoapQName headerName)
        {
            HeaderName = headerName;
        }

        #endregion

        #region Public Methods

        public ValueTask HandleAsync(
            SoapHeaderValue header,
            SoapCallContext context,
            CancellationToken cancellationToken)
        {
            _headers.Add(header);

            return ValueTask.CompletedTask;
        }

        #endregion

    }

}
