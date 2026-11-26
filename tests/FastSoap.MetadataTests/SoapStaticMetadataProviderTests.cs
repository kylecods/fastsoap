using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FastSoap.Metadata;
using Xunit;

namespace FastSoap.MetadataTests
{

    /// <summary>
    /// Tests for <see cref="SoapStaticMetadataProvider"/> and <see cref="SoapMetadataDocument"/>.
    /// </summary>
    public sealed class SoapStaticMetadataProviderTests
    {

        #region Fields

        private static readonly byte[] ValidSchemaBytes =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <xs:schema elementFormDefault="qualified" targetNamespace="urn:test" xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:element name="Ping" type="xs:string" />
            </xs:schema>
            """u8.ToArray();

        private static readonly byte[] ValidWsdlBytes =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <wsdl:definitions name="TestService" targetNamespace="urn:test" xmlns:wsdl="http://schemas.xmlsoap.org/wsdl/">
              <wsdl:types />
            </wsdl:definitions>
            """u8.ToArray();

        #endregion

        #region Public Methods

        [Fact]
        public void Create_CopiesBytesImmutablyAndComputesSha256ETagAndContentType()
        {
            byte[] mutableWsdl = ValidWsdlBytes.AsSpan().ToArray();
            byte[] mutableSchema = ValidSchemaBytes.AsSpan().ToArray();

            SoapStaticMetadataProvider provider = SoapStaticMetadataProvider.Create(
                mutableWsdl,
                [new KeyValuePair<string, byte[]>("schemas/ping-v1.xsd", mutableSchema)]);

            mutableWsdl[0] = (byte)'X';
            mutableSchema[0] = (byte)'X';

            SoapMetadataDocument wsdl = provider.GetWsdl();
            Assert.Equal(SoapMetadataDocumentKind.Wsdl, wsdl.Kind);
            Assert.Equal("wsdl", wsdl.Name);
            Assert.Equal("text/xml; charset=utf-8", wsdl.ContentType);
            Assert.True(wsdl.ContentUtf8.Span.SequenceEqual(ValidWsdlBytes));

            string expectedWsdlHash = Convert.ToHexStringLower(SHA256.HashData(ValidWsdlBytes));
            Assert.Equal(expectedWsdlHash, wsdl.Sha256Hex);
            Assert.Equal($"\"{expectedWsdlHash}\"", wsdl.ETag);
            Assert.True(wsdl.MatchesETag(wsdl.ETag));
            Assert.True(wsdl.MatchesETag($"\"other\", {wsdl.ETag}"));
            Assert.True(wsdl.MatchesETag("*"));
            Assert.False(wsdl.MatchesETag("\"deadbeef\""));
            Assert.False(wsdl.MatchesETag($"W/{wsdl.ETag}"));
            Assert.False(wsdl.MatchesETag(string.Empty));

            Assert.Equal(1, provider.SchemaCount);
            Assert.Equal(["schemas/ping-v1.xsd"], provider.SchemaNames);
            Assert.True(provider.TryGetSchema("schemas/ping-v1.xsd", out SoapMetadataDocument? schema));
            Assert.Equal(SoapMetadataDocumentKind.XmlSchema, schema.Kind);
            Assert.Equal("text/xml; charset=utf-8", schema.ContentType);
            Assert.True(schema.ContentUtf8.Span.SequenceEqual(ValidSchemaBytes));
        }

        [Fact]
        public void Create_WithGoldenWsdlAndXsdFixtures_RegistersSuccessfully()
        {
            string wsdlDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "wsdl"));
            byte[] wsdlBytes = File.ReadAllBytes(Path.Combine(wsdlDir, "echo-soap11.wsdl"));
            byte[] xsd0Bytes = File.ReadAllBytes(Path.Combine(wsdlDir, "echo-soap11.xsd0.xsd"));
            byte[] xsd1Bytes = File.ReadAllBytes(Path.Combine(wsdlDir, "echo-soap11.xsd1.xsd"));

            SoapStaticMetadataProvider provider = SoapStaticMetadataProvider.Create(
                wsdlBytes,
                [
                    new KeyValuePair<string, byte[]>("xsd1", xsd1Bytes),
                    new KeyValuePair<string, byte[]>("xsd0", xsd0Bytes)
                ]);

            Assert.Equal(2, provider.SchemaCount);
            Assert.Equal(["xsd0", "xsd1"], provider.SchemaNames);
            Assert.True(provider.TryGetSchema("xsd0", out SoapMetadataDocument? xsd0));
            Assert.True(provider.TryGetSchema("xsd1", out SoapMetadataDocument? xsd1));
            Assert.True(xsd0.ContentUtf8.Span.SequenceEqual(xsd0Bytes));
            Assert.True(xsd1.ContentUtf8.Span.SequenceEqual(xsd1Bytes));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(" xsd0")]
        [InlineData("xsd0 ")]
        [InlineData("../xsd0")]
        [InlineData("..\\xsd0")]
        [InlineData("schemas/../xsd0")]
        [InlineData("./xsd0")]
        [InlineData("/xsd0")]
        [InlineData("xsd0/")]
        [InlineData("schemas//xsd0")]
        [InlineData("xsd0?wsdl")]
        [InlineData("xsd0&evil=1")]
        [InlineData("xsd0=1")]
        [InlineData("xsd0#frag")]
        [InlineData("xsd0;param=1")]
        [InlineData("https://evil.example/xsd0")]
        [InlineData("http://localhost/xsd0")]
        [InlineData("//evil.example/xsd0")]
        [InlineData("file:///etc/passwd")]
        [InlineData("%2e%2e/xsd0")]
        [InlineData("xsd0\0.xsd")]
        [InlineData(".hidden.xsd")]
        [InlineData("trailing.")]
        [InlineData("unknown.xsd")]
        public void TryGetSchema_RejectsTraversalQueryInjectionRemoteUrlsAndUnknownNames(string? candidateKey)
        {
            SoapStaticMetadataProvider provider = SoapStaticMetadataProvider.Create(
                ValidWsdlBytes,
                [new KeyValuePair<string, byte[]>("xsd0", ValidSchemaBytes)]);

            Assert.False(provider.TryGetSchema(candidateKey, out SoapMetadataDocument? document));
            Assert.Null(document);
        }

        [Fact]
        public void Registration_RejectsInvalidXmlDtdProcessingInstructionWrongRootAndDuplicates()
        {
            byte[] dtdXml = "<!DOCTYPE definitions><wsdl:definitions xmlns:wsdl=\"http://schemas.xmlsoap.org/wsdl/\"/>"u8.ToArray();
            byte[] piXml = "<?xml version=\"1.0\" encoding=\"utf-8\"?><?evil pi?><wsdl:definitions xmlns:wsdl=\"http://schemas.xmlsoap.org/wsdl/\"/>"u8.ToArray();
            byte[] xml11 = "<?xml version=\"1.1\" encoding=\"utf-8\"?><wsdl:definitions xmlns:wsdl=\"http://schemas.xmlsoap.org/wsdl/\"/>"u8.ToArray();
            byte[] invalidUtf8 = [0xFF, 0xFE, 0x3C, 0x00];
            byte[] wrongRootForWsdl = ValidSchemaBytes;
            byte[] wrongRootForSchema = ValidWsdlBytes;

            Assert.Throws<ArgumentException>(() => SoapMetadataDocument.CreateWsdl(ReadOnlySpan<byte>.Empty));
            Assert.Throws<ArgumentException>(() => SoapMetadataDocument.CreateWsdl(dtdXml));
            Assert.Throws<ArgumentException>(() => SoapMetadataDocument.CreateWsdl(piXml));
            Assert.Throws<ArgumentException>(() => SoapMetadataDocument.CreateWsdl(xml11));
            Assert.Throws<ArgumentException>(() => SoapMetadataDocument.CreateWsdl(invalidUtf8));
            Assert.Throws<ArgumentException>(() => SoapMetadataDocument.CreateWsdl(wrongRootForWsdl));
            Assert.Throws<ArgumentException>(() => SoapMetadataDocument.CreateSchema("xsd0", wrongRootForSchema));
            Assert.Throws<ArgumentException>(() => SoapMetadataDocument.CreateSchema("../bad", ValidSchemaBytes));

            SoapMetadataDocument wsdlDoc = SoapMetadataDocument.CreateWsdl(ValidWsdlBytes);
            SoapMetadataDocument schemaDoc = SoapMetadataDocument.CreateSchema("xsd0", ValidSchemaBytes);

            Assert.Throws<ArgumentException>(() => new SoapStaticMetadataProvider(schemaDoc));
            Assert.Throws<ArgumentException>(() => new SoapStaticMetadataProvider(wsdlDoc, [wsdlDoc]));
            Assert.Throws<ArgumentException>(() => new SoapStaticMetadataProvider(wsdlDoc, [schemaDoc, schemaDoc]));
        }

        #endregion

    }

}
