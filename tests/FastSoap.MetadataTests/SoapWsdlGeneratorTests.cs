using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using FastSoap.Abstractions;
using FastSoap.Metadata;
using Xunit;

namespace FastSoap.MetadataTests
{

    /// <summary>
    /// End-to-end tests for <see cref="SoapWsdlGenerator"/> verifying determinism, ETag stability,
    /// SOAP 1.1 / SOAP 1.2 bindings, multi-namespace XSD imports, and offline <see cref="XmlSchemaSet"/> validation.
    /// </summary>
    public sealed class SoapWsdlGeneratorTests
    {

        #region Fields

        private static readonly XNamespace Soap11Ns = "http://schemas.xmlsoap.org/wsdl/soap/";
        private static readonly XNamespace Soap12Ns = "http://schemas.xmlsoap.org/wsdl/soap12/";
        private static readonly XNamespace WsdlNs = "http://schemas.xmlsoap.org/wsdl/";
        private static readonly XNamespace XsNs = "http://www.w3.org/2001/XMLSchema";

        #endregion

        #region Public Methods

        [Theory]
        [InlineData(SoapVersionFlags.Soap11)]
        [InlineData(SoapVersionFlags.Soap12)]
        [InlineData(SoapVersionFlags.Both)]
        public void GenerateWsdl_ValidatesOfflineWithZeroExternalResolutionAndEmitsExpectedBindings(SoapVersionFlags versions)
        {
            SoapServiceMetadata service = BuildPaymentServiceMetadata(versions);
            SoapMetadataAddressPolicy addressPolicy = new SoapMetadataAddressPolicy("https://payments.example.com/PaymentService.svc");

            byte[] wsdlBytesFirst = SoapWsdlGenerator.GenerateWsdl(service, addressPolicy);
            byte[] wsdlBytesSecond = SoapWsdlGenerator.GenerateWsdl(service, addressPolicy);

            Assert.True(wsdlBytesFirst.AsSpan().SequenceEqual(wsdlBytesSecond));

            SoapMetadataDocument docFirst = SoapWsdlGenerator.GenerateWsdlDocument(service, addressPolicy);
            SoapMetadataDocument docSecond = SoapWsdlGenerator.GenerateWsdlDocument(service, addressPolicy);
            Assert.Equal(docFirst.ETag, docSecond.ETag);
            Assert.Equal("text/xml; charset=utf-8", docFirst.ContentType);

            XDocument xdoc = ParseXmlStrict(wsdlBytesFirst);
            XElement definitions = xdoc.Root!;
            Assert.Equal(WsdlNs + "definitions", definitions.Name);
            Assert.Equal("PaymentService", (string?)definitions.Attribute("name"));
            Assert.Equal("urn:payments", (string?)definitions.Attribute("targetNamespace"));

            Assert.Null(definitions.GetNamespaceOfPrefix("soapenc"));
            Assert.Null(definitions.GetNamespaceOfPrefix("wsp"));
            Assert.Null(definitions.GetNamespaceOfPrefix("wsa"));

            List<XElement> schemas = new List<XElement>(definitions.Element(WsdlNs + "types")!.Elements(XsNs + "schema"));
            Assert.Equal(3, schemas.Count);
            CompileSchemasOfflineWithNullResolver(schemas);

            XElement urnPaymentsSchema = schemas.Find(s => (string?)s.Attribute("targetNamespace") == "urn:payments")!;
            List<string> importedNamespaces = [];
            foreach (XElement importElement in urnPaymentsSchema.Elements(XsNs + "import"))
            {
                importedNamespaces.Add((string)importElement.Attribute("namespace")!);
            }

            Assert.Equal(["urn:payments:common", "urn:payments:types"], importedNamespaces);

            XElement transferRequestType = FindNamedElement(urnPaymentsSchema, XsNs + "complexType", "TransferRequest");
            List<string> memberOrder = [];
            foreach (XElement member in transferRequestType.Element(XsNs + "sequence")!.Elements(XsNs + "element"))
            {
                memberOrder.Add((string)member.Attribute("name")!);
            }

            Assert.Equal(["AccountNumber", "Amount", "Metadata", "Tags"], memberOrder);

            XElement tagsElement = FindNamedElement(
                transferRequestType.Element(XsNs + "sequence")!,
                XsNs + "element",
                "Tags");
            Assert.Equal("0", (string?)tagsElement.Attribute("minOccurs"));
            Assert.Equal("unbounded", (string?)tagsElement.Attribute("maxOccurs"));
            Assert.Equal("true", (string?)tagsElement.Attribute("nillable"));

            XElement statusEnumSchema = schemas.Find(s => (string?)s.Attribute("targetNamespace") == "urn:payments:types")!;
            XElement statusSimpleType = FindNamedElement(statusEnumSchema, XsNs + "simpleType", "PaymentStatus");
            List<string> enumValues = [];
            foreach (XElement enumEl in statusSimpleType.Element(XsNs + "restriction")!.Elements(XsNs + "enumeration"))
            {
                enumValues.Add((string)enumEl.Attribute("value")!);
            }

            Assert.Equal(["Pending", "Completed", "Rejected"], enumValues);

            List<XElement> bindings = new List<XElement>(definitions.Elements(WsdlNs + "binding"));
            List<XElement> ports = new List<XElement>(definitions.Element(WsdlNs + "service")!.Elements(WsdlNs + "port"));

            bool expectSoap11 = (versions & SoapVersionFlags.Soap11) != 0;
            bool expectSoap12 = (versions & SoapVersionFlags.Soap12) != 0;
            int expectedCount = (expectSoap11 ? 1 : 0) + (expectSoap12 ? 1 : 0);

            Assert.Equal(expectedCount, bindings.Count);
            Assert.Equal(expectedCount, ports.Count);

            if (expectSoap11)
            {
                XElement soap11Binding = bindings.Find(b => b.Element(Soap11Ns + "binding") is not null)!;
                Assert.Equal("document", (string?)soap11Binding.Element(Soap11Ns + "binding")!.Attribute("style"));
                Assert.Equal("http://schemas.xmlsoap.org/soap/http", (string?)soap11Binding.Element(Soap11Ns + "binding")!.Attribute("transport"));

                XElement transferOp = FindNamedElement(soap11Binding, WsdlNs + "operation", "Transfer");
                Assert.Equal("urn:payments/IPaymentService/Transfer", (string?)transferOp.Element(Soap11Ns + "operation")!.Attribute("soapAction"));
                Assert.Equal("document", (string?)transferOp.Element(Soap11Ns + "operation")!.Attribute("style"));
                Assert.Equal("literal", (string?)transferOp.Element(WsdlNs + "input")!.Element(Soap11Ns + "body")!.Attribute("use"));
                Assert.Equal("literal", (string?)transferOp.Element(WsdlNs + "output")!.Element(Soap11Ns + "body")!.Attribute("use"));

                XElement soap11Port = ports.Find(p => p.Element(Soap11Ns + "address") is not null)!;
                Assert.Equal("https://payments.example.com/PaymentService.svc", (string?)soap11Port.Element(Soap11Ns + "address")!.Attribute("location"));
            }

            if (expectSoap12)
            {
                XElement soap12Binding = bindings.Find(b => b.Element(Soap12Ns + "binding") is not null)!;
                Assert.Equal("document", (string?)soap12Binding.Element(Soap12Ns + "binding")!.Attribute("style"));
                Assert.Equal("http://schemas.xmlsoap.org/soap/http", (string?)soap12Binding.Element(Soap12Ns + "binding")!.Attribute("transport"));

                XElement transferOp = FindNamedElement(soap12Binding, WsdlNs + "operation", "Transfer");
                Assert.Equal("urn:payments/IPaymentService/Transfer", (string?)transferOp.Element(Soap12Ns + "operation")!.Attribute("soapAction"));
                Assert.Equal("document", (string?)transferOp.Element(Soap12Ns + "operation")!.Attribute("style"));
                Assert.Equal("literal", (string?)transferOp.Element(WsdlNs + "input")!.Element(Soap12Ns + "body")!.Attribute("use"));
                Assert.Equal("literal", (string?)transferOp.Element(WsdlNs + "output")!.Element(Soap12Ns + "body")!.Attribute("use"));

                XElement soap12Port = ports.Find(p => p.Element(Soap12Ns + "address") is not null)!;
                Assert.Equal("https://payments.example.com/PaymentService.svc", (string?)soap12Port.Element(Soap12Ns + "address")!.Attribute("location"));
            }
        }

        [Fact]
        public void GenerateStaticProvider_EmitsSeparateXsdDocumentsWithConfiguredLocationsAndValidatesOffline()
        {
            SoapServiceMetadata service = BuildPaymentServiceMetadata(SoapVersionFlags.Both);
            SoapMetadataAddressPolicy addressPolicy = new SoapMetadataAddressPolicy("https://payments.example.com/PaymentService.svc");

            SoapStaticMetadataProvider queryProvider = SoapWsdlGenerator.GenerateStaticProvider(
                service,
                addressPolicy,
                useQuerySchemaLocations: true);

            Assert.Equal(3, queryProvider.SchemaCount);
            Assert.Equal(["xsd0", "xsd1", "xsd2"], queryProvider.SchemaNames);

            XDocument queryWsdl = ParseXmlStrict(queryProvider.GetWsdl().ContentUtf8.Span);
            List<XElement> wsdlImports = new List<XElement>(
                queryWsdl.Root!
                    .Element(WsdlNs + "types")!
                    .Element(XsNs + "schema")!
                    .Elements(XsNs + "import"));

            Assert.Equal(3, wsdlImports.Count);
            Assert.Equal("urn:payments", (string?)wsdlImports[0].Attribute("namespace"));
            Assert.Equal("https://payments.example.com/PaymentService.svc?xsd=xsd0", (string?)wsdlImports[0].Attribute("schemaLocation"));
            Assert.Equal("urn:payments:common", (string?)wsdlImports[1].Attribute("namespace"));
            Assert.Equal("https://payments.example.com/PaymentService.svc?xsd=xsd1", (string?)wsdlImports[1].Attribute("schemaLocation"));
            Assert.Equal("urn:payments:types", (string?)wsdlImports[2].Attribute("namespace"));
            Assert.Equal("https://payments.example.com/PaymentService.svc?xsd=xsd2", (string?)wsdlImports[2].Attribute("schemaLocation"));

            SoapStaticMetadataProvider relativeProvider = SoapWsdlGenerator.GenerateStaticProvider(
                service,
                addressPolicy,
                useQuerySchemaLocations: false);

            Assert.Equal(["xsd0.xsd", "xsd1.xsd", "xsd2.xsd"], relativeProvider.SchemaNames);

            List<XElement> externalSchemas = new List<XElement>(relativeProvider.SchemaCount);
            foreach (string schemaName in relativeProvider.SchemaNames)
            {
                Assert.True(relativeProvider.TryGetSchema(schemaName, out SoapMetadataDocument? doc));
                externalSchemas.Add(ParseXmlStrict(doc.ContentUtf8.Span).Root!);
            }

            CompileSchemasOfflineWithNullResolver(externalSchemas);
        }

        [Fact]
        public void FromServiceMetadata_WithCrossNamespaceOperationsAndGlobalTypedElements_ValidatesOffline()
        {
            SoapSchemaType itemType = SoapSchemaType.Complex(
                new SoapQName("urn:items", "ItemRecord"),
                [
                    SoapSchemaElement.CreateMember("Id", SoapSchemaType.Int, minOccurs: 1, maxOccurs: 1, isNillable: false),
                    SoapSchemaElement.CreateMember("Code", SoapSchemaType.Primitive("string"), minOccurs: 0, maxOccurs: 1, isNillable: true)
                ]);

            SoapOperationDescriptor opDescriptor = new SoapOperationDescriptor(
                "LookupItem",
                "urn:catalog/LookupItem",
                null,
                new SoapQName("urn:catalog", "LookupItem"),
                new SoapQName("urn:items", "LookupItemResponse"));

            SoapOperationMetadata operation = new SoapOperationMetadata(
                opDescriptor,
                SoapSchemaElement.CreateGlobal(opDescriptor.RequestElement, SoapSchemaType.String),
                SoapSchemaElement.CreateGlobal(opDescriptor.ResponseElement, itemType, isNillable: true));

            SoapServiceMetadata service = new SoapServiceMetadata(
                "CatalogService",
                "urn:catalog",
                [operation],
                SoapVersionFlags.Soap11);

            SoapMetadataAddressPolicy addressPolicy = new SoapMetadataAddressPolicy("http://catalog.example.com/Catalog.svc");
            SoapStaticMetadataProvider provider = SoapStaticMetadataProvider.FromServiceMetadata(service, addressPolicy);

            Assert.Equal(2, provider.SchemaCount);
            Assert.Equal(["xsd0", "xsd1"], provider.SchemaNames);

            XDocument wsdlDoc = ParseXmlStrict(provider.GetWsdl().ContentUtf8.Span);
            XElement definitions = wsdlDoc.Root!;
            Assert.Equal("urn:items", definitions.GetNamespaceOfPrefix("ns0")?.NamespaceName);

            XElement outputMessage = FindNamedElement(definitions, WsdlNs + "message", "ICatalogService_LookupItem_OutputMessage");
            Assert.Equal("ns0:LookupItemResponse", (string?)outputMessage.Element(WsdlNs + "part")!.Attribute("element"));

            SoapStaticMetadataProvider relativeProvider = SoapWsdlGenerator.GenerateStaticProvider(
                service,
                addressPolicy,
                useQuerySchemaLocations: false);

            CompileSchemasThroughOfflineProviderResolver(relativeProvider);
        }

        #endregion

        #region Private Methods

        private static SoapServiceMetadata BuildPaymentServiceMetadata(SoapVersionFlags versions)
        {
            SoapSchemaType paymentStatusEnum = SoapSchemaType.Enum(
                new SoapQName("urn:payments:types", "PaymentStatus"),
                ["Pending", "Completed", "Rejected"]);

            SoapSchemaType transferMetadataType = SoapSchemaType.Complex(
                new SoapQName("urn:payments:common", "TransferMetadata"),
                [
                    SoapSchemaElement.CreateMember("CorrelationId", SoapSchemaType.String, minOccurs: 1, maxOccurs: 1, isNillable: false),
                    SoapSchemaElement.CreateMember("Reference", SoapSchemaType.String, minOccurs: 0, maxOccurs: 1, isNillable: true)
                ]);

            SoapSchemaType transferRequestType = SoapSchemaType.Complex(
                new SoapQName("urn:payments", "TransferRequest"),
                [
                    SoapSchemaElement.CreateMember("AccountNumber", SoapSchemaType.String, minOccurs: 1, maxOccurs: 1, isNillable: false),
                    SoapSchemaElement.CreateMember("Amount", SoapSchemaType.Decimal, minOccurs: 1, maxOccurs: 1, isNillable: false),
                    SoapSchemaElement.CreateMember("Metadata", transferMetadataType, minOccurs: 0, maxOccurs: 1, isNillable: true),
                    SoapSchemaElement.CreateMember("Tags", SoapSchemaType.String, minOccurs: 0, maxOccurs: null, isNillable: true)
                ]);

            SoapSchemaType transferResultType = SoapSchemaType.Complex(
                new SoapQName("urn:payments", "TransferResult"),
                [
                    SoapSchemaElement.CreateMember("TransactionId", SoapSchemaType.String, minOccurs: 1, maxOccurs: 1, isNillable: false),
                    SoapSchemaElement.CreateMember("Successful", SoapSchemaType.Boolean, minOccurs: 1, maxOccurs: 1, isNillable: false),
                    SoapSchemaElement.CreateMember("ProcessedAtUtc", SoapSchemaType.DateTime, minOccurs: 1, maxOccurs: 1, isNillable: false),
                    SoapSchemaElement.CreateMember("Status", paymentStatusEnum, minOccurs: 1, maxOccurs: 1, isNillable: false)
                ]);

            SoapOperationDescriptor transferDescriptor = new SoapOperationDescriptor(
                "Transfer",
                "urn:payments/IPaymentService/Transfer",
                "urn:payments/IPaymentService/TransferResponse",
                new SoapQName("urn:payments", "Transfer"),
                new SoapQName("urn:payments", "TransferResponse"));

            SoapOperationMetadata transferOperation = new SoapOperationMetadata(
                transferDescriptor,
                SoapSchemaElement.CreateWrapped(
                    transferDescriptor.RequestElement,
                    [SoapSchemaElement.CreateMember("request", transferRequestType, minOccurs: 0, maxOccurs: 1, isNillable: true)]),
                SoapSchemaElement.CreateWrapped(
                    transferDescriptor.ResponseElement,
                    [SoapSchemaElement.CreateMember("TransferResult", transferResultType, minOccurs: 0, maxOccurs: 1, isNillable: true)]));

            return new SoapServiceMetadata(
                "PaymentService",
                "urn:payments",
                [transferOperation],
                versions,
                "IPaymentService");
        }

        private static void CompileSchemasOfflineWithNullResolver(IReadOnlyList<XElement> schemaElements)
        {
            XmlSchemaSet schemaSet = new XmlSchemaSet
            {
                XmlResolver = null
            };

            List<string> validationIssues = [];
            schemaSet.ValidationEventHandler += (_, args) =>
            {
                validationIssues.Add($"{args.Severity}: {args.Message}");
            };

            XmlReaderSettings readerSettings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };

            foreach (XElement schemaElement in schemaElements)
            {
                XElement sanitizedSchema = new XElement(schemaElement);
                foreach (XElement import in sanitizedSchema.Elements(XsNs + "import"))
                {
                    import.Attribute("schemaLocation")?.Remove();
                }

                using StringReader stringReader = new StringReader(sanitizedSchema.ToString(SaveOptions.DisableFormatting));
                using XmlReader xmlReader = XmlReader.Create(stringReader, readerSettings);
                XmlSchema? parsed = XmlSchema.Read(xmlReader, (_, args) =>
                {
                    validationIssues.Add($"{args.Severity}: {args.Message}");
                });

                Assert.NotNull(parsed);
                schemaSet.Add(parsed);
            }

            schemaSet.Compile();
            Assert.True(schemaSet.IsCompiled);
            Assert.Empty(validationIssues);
        }

        private static void CompileSchemasThroughOfflineProviderResolver(SoapStaticMetadataProvider provider)
        {
            OfflineMetadataXmlResolver offlineResolver = new OfflineMetadataXmlResolver(provider);
            XmlSchemaSet schemaSet = new XmlSchemaSet
            {
                XmlResolver = offlineResolver
            };

            List<string> validationIssues = [];
            schemaSet.ValidationEventHandler += (_, args) =>
            {
                validationIssues.Add($"{args.Severity}: {args.Message}");
            };

            XmlReaderSettings readerSettings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };

            foreach (string schemaName in provider.SchemaNames)
            {
                Assert.True(provider.TryGetSchema(schemaName, out SoapMetadataDocument? doc));
                using MemoryStream stream = new MemoryStream(doc.ContentUtf8.ToArray());
                using XmlReader reader = XmlReader.Create(stream, readerSettings, $"https://offline.local/{schemaName}");
                XmlSchema? schema = XmlSchema.Read(reader, (_, args) =>
                {
                    validationIssues.Add($"{args.Severity}: {args.Message}");
                });

                Assert.NotNull(schema);
                schemaSet.Add(schema);
            }

            schemaSet.Compile();
            Assert.True(schemaSet.IsCompiled);
            Assert.Empty(validationIssues);
        }

        private static XElement FindNamedElement(XElement container, XName elementName, string nameAttributeValue)
        {
            foreach (XElement child in container.Elements(elementName))
            {
                if (string.Equals((string?)child.Attribute("name"), nameAttributeValue, StringComparison.Ordinal))
                {
                    return child;
                }
            }

            throw new Xunit.Sdk.XunitException($"Element '{elementName}' with name='{nameAttributeValue}' was not found.");
        }

        private static XDocument ParseXmlStrict(ReadOnlySpan<byte> utf8Bytes)
        {
            XmlReaderSettings settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };

            using MemoryStream stream = new MemoryStream(utf8Bytes.ToArray());
            using XmlReader reader = XmlReader.Create(stream, settings);
            return XDocument.Load(reader, LoadOptions.None);
        }

        #endregion

        #region Nested Types

        private sealed class OfflineMetadataXmlResolver : XmlResolver
        {

            #region Fields

            private readonly SoapStaticMetadataProvider _provider;

            #endregion

            #region Constructors

            public OfflineMetadataXmlResolver(SoapStaticMetadataProvider provider)
            {
                _provider = provider;
            }

            #endregion

            #region Public Methods

            public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
            {
                if (!string.Equals(absoluteUri.Host, "offline.local", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Blocked external URI resolution attempt: {absoluteUri}");
                }

                string schemaName = absoluteUri.AbsolutePath.TrimStart('/');
                if (!_provider.TryGetSchema(schemaName, out SoapMetadataDocument? schema))
                {
                    throw new FileNotFoundException($"Schema '{schemaName}' was not registered in SoapStaticMetadataProvider.");
                }

                return new MemoryStream(schema.ContentUtf8.ToArray());
            }

            #endregion

        }

        #endregion

    }

}
