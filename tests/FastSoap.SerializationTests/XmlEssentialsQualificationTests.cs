using System;
using System.Collections.Generic;
using System.Text;

using CloudNimble.Text.Xml;
using CloudNimble.Text.Xml.Serialization;

using FastSoap.Abstractions;
using FastSoap.Core.Serialization;

using Xunit;

namespace FastSoap.SerializationTests
{

    public enum TransferStatus
    {
        Pending = 0,
        Completed = 1,
        Failed = 2
    }

    [XmlRoot(ElementName = "BatchTransfer", Namespace = "urn:payments")]
    public sealed class BatchTransferWire
    {

        #region Properties

        [XmlElement(ElementName = "BatchId", Namespace = "urn:payments", Order = 0)]
        public string? BatchId { get; set; }

        [XmlArray(ElementName = "Items", Namespace = "urn:payments", Order = 1)]
        [XmlArrayItem(ElementName = "TransferItem", Namespace = "urn:payments")]
        public List<TransferItem>? Items { get; set; }

        #endregion

    }

    [XmlSerializable<BatchTransferWire>]
    [XmlSerializable<PingWire>]
    [XmlSerializable<RequiredPropsWire>]
    [XmlSerializable<ScalarTypesWire>]
    [XmlSerializable<TransferOuterWire>]
    public partial class C1QualificationXmlContext : XmlSerializerContext
    {
    }

    [XmlRoot(ElementName = "Ping", Namespace = "urn:test")]
    public sealed class PingWire
    {
    }

    [XmlRoot(ElementName = "RequiredProps", Namespace = "urn:test")]
    public sealed class RequiredPropsWire
    {

        #region Properties

        [XmlElement(ElementName = "Amount", Namespace = "urn:test", Order = 1)]
        public required decimal Amount { get; init; }

        [XmlElement(ElementName = "Reference", Namespace = "urn:test", Order = 0)]
        public required string Reference { get; init; }

        #endregion

    }

    [XmlRoot(ElementName = "ScalarTypes", Namespace = "urn:test")]
    public sealed class ScalarTypesWire
    {

        #region Properties

        [XmlElement(ElementName = "Amount", Namespace = "urn:test", Order = 0)]
        public decimal Amount { get; set; }

        [XmlElement(ElementName = "OptionalNote", Namespace = "urn:test", IsNullable = true, Order = 3)]
        public string? OptionalNote { get; set; }

        [XmlElement(ElementName = "ProcessedAtUtc", Namespace = "urn:test", Order = 1)]
        public DateTime ProcessedAtUtc { get; set; }

        [XmlElement(ElementName = "Status", Namespace = "urn:test", Order = 2)]
        public TransferStatus Status { get; set; }

        #endregion

    }

    public sealed class TransferItem
    {

        #region Properties

        [XmlElement(ElementName = "AccountNumber", Namespace = "urn:payments", Order = 0)]
        public string? AccountNumber { get; set; }

        [XmlElement(ElementName = "Amount", Namespace = "urn:payments", Order = 1)]
        public decimal Amount { get; set; }

        [XmlElement(ElementName = "Note", Namespace = "urn:payments", IsNullable = true, Order = 2)]
        public string? Note { get; set; }

        #endregion

    }

    public sealed class TransferMetadata
    {

        #region Properties

        [XmlElement(ElementName = "CorrelationId", Namespace = "urn:detail", Order = 0)]
        public string? CorrelationId { get; set; }

        #endregion

    }

    [XmlRoot(ElementName = "TransferOuter", Namespace = "urn:payments")]
    public sealed class TransferOuterWire
    {

        #region Properties

        [XmlElement(ElementName = "Metadata", Namespace = "urn:detail", Order = 1)]
        public TransferMetadata? Metadata { get; set; }

        [XmlElement(ElementName = "Reference", Namespace = "urn:payments", Order = 0)]
        public string? Reference { get; set; }

        #endregion

    }

    /// <summary>
    /// ADR-001 Section 3.2 Fail-Fast C1 Corpus Qualification tests for pinned <c>XmlEssentials 1.0.0</c>
    /// and internal <see cref="XmlEssentialsAdapter"/> configuration.
    /// </summary>
    public sealed class XmlEssentialsQualificationTests
    {

        #region Fields

        private static readonly C1QualificationXmlContext s_strictContext = new C1QualificationXmlContext(
            XmlEssentialsAdapter.CreateStrictSerializerOptions());

        #endregion

        #region Public Methods

        [Fact]
        public void C1_CollectionsWithPopulatedAndNilItems_RoundTripCorrectly()
        {
            byte[] xml = """
                <BatchTransfer xmlns="urn:payments" xmlns:i="http://www.w3.org/2001/XMLSchema-instance">
                  <BatchId>BATCH-01</BatchId>
                  <Items>
                    <TransferItem>
                      <AccountNumber>123456</AccountNumber>
                      <Amount>100.50</Amount>
                      <Note>Invoice</Note>
                    </TransferItem>
                    <TransferItem i:nil="true" />
                    <TransferItem>
                      <AccountNumber>789012</AccountNumber>
                      <Amount>25.00</Amount>
                      <Note i:nil="true" />
                    </TransferItem>
                  </Items>
                </BatchTransfer>
                """u8.ToArray();

            Utf8XmlReader reader = new Utf8XmlReader(xml, XmlEssentialsAdapter.CreateReaderOptions(new SoapEndpointOptions()));
            Assert.True(XmlEssentialsAdapter.ReadNextContentToken(ref reader));

            BatchTransferWire? result = XmlSerializer.Deserialize(ref reader, s_strictContext.BatchTransferWire);

            Assert.NotNull(result);
            Assert.Equal("BATCH-01", result.BatchId);
            Assert.NotNull(result.Items);
            Assert.Equal(3, result.Items.Count);
            Assert.NotNull(result.Items[0]);
            Assert.Equal("123456", result.Items[0]!.AccountNumber);
            Assert.Equal(100.50m, result.Items[0]!.Amount);
            Assert.Equal("Invoice", result.Items[0]!.Note);
            Assert.Null(result.Items[1]);
            Assert.NotNull(result.Items[2]);
            Assert.Equal("789012", result.Items[2]!.AccountNumber);
            Assert.Null(result.Items[2]!.Note);
        }

        [Fact]
        public void C1_ControlCharacter0x0F_ThrowsXmlException()
        {
            byte[] xml = [.. "<ScalarTypes xmlns=\"urn:test\"><Amount>1</Amount><OptionalNote>bad"u8, 0x0F, .. "har</OptionalNote></ScalarTypes>"u8];

            Assert.Throws<XmlException>(() =>
            {
                Utf8XmlReader reader = new Utf8XmlReader(xml, XmlEssentialsAdapter.CreateReaderOptions(new SoapEndpointOptions()));
                reader.Read();
                _ = XmlSerializer.Deserialize(ref reader, s_strictContext.ScalarTypesWire);
            });
        }

        [Fact]
        public void C1_DuplicatePropertyAndUnmappedMember_RejectedBySharedSerializerOptions()
        {
            byte[] duplicateXml = """
                <ScalarTypes xmlns="urn:test">
                  <Amount>10.00</Amount>
                  <Amount>20.00</Amount>
                </ScalarTypes>
                """u8.ToArray();

            Assert.Throws<XmlException>(() =>
            {
                Utf8XmlReader reader = new Utf8XmlReader(duplicateXml, XmlEssentialsAdapter.CreateReaderOptions(new SoapEndpointOptions()));
                reader.Read();
                _ = XmlSerializer.Deserialize(ref reader, s_strictContext.ScalarTypesWire);
            });

            byte[] unmappedXml = """
                <ScalarTypes xmlns="urn:test">
                  <Amount>10.00</Amount>
                  <Unexpected>boom</Unexpected>
                </ScalarTypes>
                """u8.ToArray();

            Assert.Throws<XmlException>(() =>
            {
                Utf8XmlReader reader = new Utf8XmlReader(unmappedXml, XmlEssentialsAdapter.CreateReaderOptions(new SoapEndpointOptions()));
                reader.Read();
                _ = XmlSerializer.Deserialize(ref reader, s_strictContext.ScalarTypesWire);
            });
        }

        [Fact]
        public void C1_EnumsUtcDateTimeDecimalAndNil_DeserializeAndSerializeAccurately()
        {
            byte[] xml = """
                <ScalarTypes xmlns="urn:test" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
                  <Amount>-1234.5678</Amount>
                  <ProcessedAtUtc>2026-11-26T12:00:00Z</ProcessedAtUtc>
                  <Status>Completed</Status>
                  <OptionalNote xsi:nil="true" />
                </ScalarTypes>
                """u8.ToArray();

            Utf8XmlReader reader = new Utf8XmlReader(xml, XmlEssentialsAdapter.CreateReaderOptions(new SoapEndpointOptions()));
            Assert.True(XmlEssentialsAdapter.ReadNextContentToken(ref reader));

            ScalarTypesWire? result = XmlSerializer.Deserialize(ref reader, s_strictContext.ScalarTypesWire);

            Assert.NotNull(result);
            Assert.Equal(-1234.5678m, result.Amount);
            Assert.Equal(new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc), result.ProcessedAtUtc);
            Assert.Equal(DateTimeKind.Utc, result.ProcessedAtUtc.Kind);
            Assert.Equal(TransferStatus.Completed, result.Status);
            Assert.Null(result.OptionalNote);
        }

        [Fact]
        public void C1_MissingRequiredProperties_DocumentsExactXmlEssentials100Behavior()
        {
            // Document observed XmlEssentials 1.0.0 behavior when XML omits C# `required` properties.
            byte[] xml = "<RequiredProps xmlns=\"urn:test\"><Reference>ONLY-REF</Reference></RequiredProps>"u8.ToArray();

            XmlException ex = Assert.Throws<XmlException>(() =>
            {
                Utf8XmlReader reader = new Utf8XmlReader(xml, XmlEssentialsAdapter.CreateReaderOptions(new SoapEndpointOptions()));
                Assert.True(XmlEssentialsAdapter.ReadNextContentToken(ref reader));
                _ = XmlSerializer.Deserialize(ref reader, s_strictContext.RequiredPropsWire);
            });

            Assert.Contains("Amount", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void C1_MultiNamespaceNestedDtos_RespectInheritedEnvelopeScope()
        {
            byte[] xml = """
                <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/" xmlns:p="urn:payments" xmlns:d="urn:detail">
                  <s:Body>
                    <p:TransferOuter>
                      <p:Reference>REF-99</p:Reference>
                      <d:Metadata>
                        <d:CorrelationId>CORR-1</d:CorrelationId>
                      </d:Metadata>
                    </p:TransferOuter>
                  </s:Body>
                </s:Envelope>
                """u8.ToArray();

            Utf8XmlReader reader = new Utf8XmlReader(xml, XmlEssentialsAdapter.CreateReaderOptions(new SoapEndpointOptions()));
            Assert.True(XmlEssentialsAdapter.ReadNextContentToken(ref reader));
            Assert.True(XmlEssentialsAdapter.ReadNextContentToken(ref reader));
            Assert.True(XmlEssentialsAdapter.ReadNextContentToken(ref reader));

            TransferOuterWire? result = XmlSerializer.Deserialize(ref reader, s_strictContext.TransferOuterWire);

            Assert.NotNull(result);
            Assert.Equal("REF-99", result.Reference);
            Assert.NotNull(result.Metadata);
            Assert.Equal("CORR-1", result.Metadata.CorrelationId);
        }

        [Fact]
        public void C1_ProcessingInstructionInsideDeserializedBody_DocumentsExactXmlEssentials100Behavior()
        {
            // Document observed XmlEssentials 1.0.0 behavior for <?pi?> inside a deserialized body element.
            byte[] xml = "<ScalarTypes xmlns=\"urn:test\"><Amount>42.50</Amount><?pi payload?><Status>Pending</Status></ScalarTypes>"u8.ToArray();
            Utf8XmlReader reader = new Utf8XmlReader(xml, XmlEssentialsAdapter.CreateReaderOptions(new SoapEndpointOptions()));
            Assert.True(XmlEssentialsAdapter.ReadNextContentToken(ref reader));

            ScalarTypesWire? result = null;
            Exception? observedException = null;
            try
            {
                result = XmlSerializer.Deserialize(ref reader, s_strictContext.ScalarTypesWire);
            }
            catch (Exception ex)
            {
                observedException = ex;
            }

            // Record exact behavior: XmlEssentials 1.0.0 XmlSerializer.Deserialize skips ProcessingInstruction tokens
            // inside body elements without throwing! Therefore FastSoap MUST detect/reject PIs explicitly if needed.
            Assert.Null(observedException);
            Assert.NotNull(result);
            Assert.Equal(42.50m, result.Amount);
            Assert.Equal(TransferStatus.Pending, result.Status);
        }

        [Fact]
        public void C1_SelfClosingEmptyBodyElement_DeserializesAndAdvancesReaderCorrectly()
        {
            byte[] xml = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><Ping xmlns=\"urn:test\" /></s:Body></s:Envelope>"u8.ToArray();
            Utf8XmlReader reader = new Utf8XmlReader(xml, XmlEssentialsAdapter.CreateReaderOptions(new SoapEndpointOptions()));

            Assert.True(XmlEssentialsAdapter.ReadNextContentToken(ref reader));
            Assert.True(XmlEssentialsAdapter.ReadNextContentToken(ref reader));
            Assert.True(XmlEssentialsAdapter.ReadNextContentToken(ref reader));
            Assert.Equal(XmlTokenType.StartElement, reader.TokenType);
            Assert.True(reader.IsEmptyElement);

            PingWire? result = XmlSerializer.Deserialize(ref reader, s_strictContext.PingWire);
            Assert.NotNull(result);

            Assert.True(XmlEssentialsAdapter.ReadNextContentToken(ref reader));
            Assert.Equal(XmlTokenType.EndElement, reader.TokenType);
            Assert.True(reader.LocalNameEquals("Body"u8));
        }

        [Fact]
        public void C1_Utf8BomAndNonAsciiEmoji_DeserializeWithoutTranscodingLoss()
        {
            byte[] bom = [0xEF, 0xBB, 0xBF];
            byte[] body = Encoding.UTF8.GetBytes("<ScalarTypes xmlns=\"urn:test\"><Amount>1.25</Amount><OptionalNote>Hello, 世界! € — café 🚀</OptionalNote></ScalarTypes>");
            byte[] xmlWithBom = [.. bom, .. body];

            Utf8XmlReader reader = new Utf8XmlReader(xmlWithBom, XmlEssentialsAdapter.CreateReaderOptions(new SoapEndpointOptions()));
            Assert.True(XmlEssentialsAdapter.ReadNextContentToken(ref reader));

            ScalarTypesWire? result = XmlSerializer.Deserialize(ref reader, s_strictContext.ScalarTypesWire);

            Assert.NotNull(result);
            Assert.Equal(1.25m, result.Amount);
            Assert.Equal("Hello, 世界! € — café 🚀", result.OptionalNote);
        }

        [Fact]
        public void XmlEssentialsAdapter_ConfiguresStrictQuotasAndSerializerOptions()
        {
            SoapEndpointOptions endpointOptions = new SoapEndpointOptions
            {
                MaxAttributesPerElement = 12,
                MaxRequestBytes = 8192,
                MaxXmlDepth = 16
            };

            XmlReaderOptions readerOptions = XmlEssentialsAdapter.CreateReaderOptions(endpointOptions);

            Assert.False(readerOptions.AllowMultipleRoots);
            Assert.Equal(12, readerOptions.MaxAttributeCount);
            Assert.Equal(16, readerOptions.MaxDepth);
            Assert.Equal(8192, readerOptions.MaxDocumentByteCount);
            Assert.Equal(1024, readerOptions.MaxNameByteCount);
            Assert.Equal(8192, readerOptions.MaxTextByteCount);

            XmlSerializerOptions serializerOptions = XmlEssentialsAdapter.SharedSerializerOptions;
            Assert.False(serializerOptions.AllowDuplicateProperties);
            Assert.True(serializerOptions.OmitXmlDeclaration);
            Assert.Equal(XmlUnmappedMemberHandling.Disallow, serializerOptions.UnmappedMemberHandling);
        }

        #endregion

    }

}
