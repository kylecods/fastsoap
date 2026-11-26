using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using CloudNimble.Text.Xml.Serialization;

using FastSoap.Abstractions;
using FastSoap.Core.Serialization;

namespace FastSoap.ProtocolTests
{

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

    public sealed class FakeServiceProvider : IServiceProvider
    {

        #region Public Methods

        public object? GetService(Type serviceType)
        {
            return null;
        }

        #endregion

    }

    [XmlSerializable<EchoHeaderRequestWire>]
    [XmlSerializable<EchoHeaderResponseWire>]
    [XmlSerializable<EchoStringRequestWire>]
    [XmlSerializable<EchoStringResponseWire>]
    [XmlSerializable<TransferRequestWire>]
    [XmlSerializable<TransferResponseWire>]
    public partial class ProtocolXmlContext : XmlSerializerContext
    {
    }

    public sealed class RecordingHeaderHandler : ISoapHeaderHandler
    {

        #region Fields

        private readonly List<SoapHeaderValue> _handledHeaders = new List<SoapHeaderValue>();

        #endregion

        #region Properties

        public IReadOnlyList<SoapHeaderValue> HandledHeaders => _handledHeaders;

        public SoapQName HeaderName { get; }

        public int InvocationCount => _handledHeaders.Count;

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
            _handledHeaders.Add(header);

            return ValueTask.CompletedTask;
        }

        #endregion

    }

    public sealed class TransferMetadataWire
    {

        #region Properties

        [XmlElement(ElementName = "CorrelationId", Namespace = "urn:payments", Order = 0)]
        public string? CorrelationId { get; set; }

        [XmlElement(ElementName = "Reference", Namespace = "urn:payments", Order = 1)]
        public string? Reference { get; set; }

        #endregion

    }

    public sealed class TransferRequestDto
    {

        #region Properties

        [XmlElement(ElementName = "AccountNumber", Namespace = "urn:payments", Order = 0)]
        public string? AccountNumber { get; set; }

        [XmlElement(ElementName = "Amount", Namespace = "urn:payments", Order = 1)]
        public decimal Amount { get; set; }

        [XmlElement(ElementName = "Currency", Namespace = "urn:payments", Order = 2)]
        public string? Currency { get; set; }

        [XmlElement(ElementName = "Metadata", Namespace = "urn:payments", IsNullable = true, Order = 3)]
        public TransferMetadataWire? Metadata { get; set; }

        [XmlElement(ElementName = "Note", Namespace = "urn:payments", IsNullable = true, Order = 4)]
        public string? Note { get; set; }

        #endregion

    }

    [XmlRoot(ElementName = "Transfer", Namespace = "urn:payments")]
    public sealed class TransferRequestWire
    {

        #region Properties

        [XmlElement(ElementName = "request", Namespace = "urn:payments")]
        public TransferRequestDto? Request { get; set; }

        #endregion

    }

    [XmlRoot(ElementName = "TransferResponse", Namespace = "urn:payments")]
    public sealed class TransferResponseWire
    {

        #region Properties

        [XmlElement(ElementName = "TransferResult", Namespace = "urn:payments")]
        public TransferResultDto? TransferResult { get; set; }

        #endregion

    }

    public sealed class TransferResultDto
    {

        #region Properties

        [XmlElement(ElementName = "ProcessedAtUtc", Namespace = "urn:payments", Order = 2)]
        public string? ProcessedAtUtc { get; set; }

        [XmlElement(ElementName = "Status", Namespace = "urn:payments", Order = 3)]
        public string? Status { get; set; }

        [XmlElement(ElementName = "Successful", Namespace = "urn:payments", Order = 1)]
        public bool Successful { get; set; }

        [XmlElement(ElementName = "TransactionId", Namespace = "urn:payments", Order = 0)]
        public string? TransactionId { get; set; }

        #endregion

    }

    internal static class ProtocolTestFixtures
    {

        #region Fields

        private static readonly ProtocolXmlContext s_context = new ProtocolXmlContext(
            XmlEssentialsAdapter.SharedSerializerOptions);

        #endregion

        #region Properties

        public static ProtocolXmlContext Context => s_context;

        #endregion

        #region Public Methods

        public static SoapOperationDescriptor CreateEchoDescriptor()
        {
            return new SoapOperationDescriptor(
                "EchoString",
                "http://tempuri.org/IEchoService/EchoString",
                "http://tempuri.org/IEchoService/EchoStringResponse",
                new SoapQName("http://tempuri.org/", "EchoString"),
                new SoapQName("http://tempuri.org/", "EchoStringResponse"));
        }

        public static SoapOperationDescriptor CreateEchoHeaderDescriptor()
        {
            return new SoapOperationDescriptor(
                "EchoWithMessageContract",
                "http://tempuri.org/IServiceWithMessageBodyAndHeader/EchoWithMessageContract",
                "http://tempuri.org/IServiceWithMessageBodyAndHeader/EchoWithMessageContractResponse",
                new SoapQName("http://tempuri.org/", "EchoHeaderRequest"),
                new SoapQName("http://tempuri.org/", "EchoHeaderResponse"));
        }

        public static SoapOperationDescriptor CreateTransferDescriptor()
        {
            return new SoapOperationDescriptor(
                "Transfer",
                "urn:payments/IPaymentService/Transfer",
                "urn:payments/IPaymentService/TransferResponse",
                new SoapQName("urn:payments", "Transfer"),
                new SoapQName("urn:payments", "TransferResponse"));
        }

        public static string GetFixturePath(string relativePath)
        {
            DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current is not null)
            {
                string candidate = Path.Combine(current.FullName, "tests", "fixtures");
                if (Directory.Exists(candidate))
                {
                    return Path.Combine(candidate, relativePath);
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate tests/fixtures directory.");
        }

        public static byte[] ReadFixtureBytes(string relativePath)
        {
            return File.ReadAllBytes(GetFixturePath(relativePath));
        }

        public static string ReadFixtureText(string relativePath)
        {
            return File.ReadAllText(GetFixturePath(relativePath), Encoding.UTF8);
        }

        #endregion

    }

}
