using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

using CloudNimble.Text.Xml;
using CloudNimble.Text.Xml.Serialization;

using Xunit;

namespace FastSoap.CompatibilityTests
{

    /// <summary>
    /// Verifies FastSoap against every entry in the CoreWCF 1.9.2 golden wire corpus
    /// (<c>tests/fixtures/manifest.json</c>), reporting exact passed, failed, excluded, and pending counts separately.
    /// </summary>
    public sealed class GoldenCorpusTests
    {

        #region Fields

        private readonly ITestOutputHelper _output;

        #endregion

        #region Constructors

        public GoldenCorpusTests(ITestOutputHelper output)
        {
            _output = output;
        }

        #endregion

        #region Public Methods

        [Fact]
        public async Task CoreWcf192GoldenCorpus_All63ManifestEntries_VerifiedWithExactIntegrityAndOutcomeReporting()
        {
            string fixturesRoot = CompatibilityServerHost.GetFixturesRoot();
            string manifestPath = Path.Combine(fixturesRoot, "manifest.json");
            Assert.True(File.Exists(manifestPath));

            ManifestDocument manifest = JsonSerializer.Deserialize<ManifestDocument>(
                await File.ReadAllTextAsync(manifestPath, TestContext.Current.CancellationToken),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

            Assert.Equal(63, manifest.Entries.Count);

            foreach (ManifestEntry entry in manifest.Entries)
            {
                foreach (KeyValuePair<string, string> fileHash in entry.Sha256ByFile)
                {
                    string fullPath = Path.Combine(fixturesRoot, fileHash.Key.Replace('/', Path.DirectorySeparatorChar));
                    Assert.True(File.Exists(fullPath), $"Missing fixture file: {fileHash.Key}");
                    byte[] bytes = await File.ReadAllBytesAsync(fullPath, TestContext.Current.CancellationToken);
                    string actualSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
                    Assert.Equal(fileHash.Value, actualSha256);
                }
            }

            await using CompatibilityServerHost host = await CompatibilityServerHost.StartAsync();

            int positivePassed = 0;
            int positivePendingOrPartial = 0;
            int negativeRejected = 0;
            int serializerPassed = 0;
            int serializerPendingOrPartial = 0;
            int wsdlPassed = 0;
            int unexpectedFailures = 0;

            foreach (ManifestEntry entry in manifest.Entries)
            {
                if (string.Equals(entry.Category, "positive", StringComparison.Ordinal))
                {
                    bool verified = await VerifyPositiveWireFixtureAsync(host, fixturesRoot, entry);
                    if (verified)
                    {
                        positivePassed++;
                    }
                    else
                    {
                        positivePendingOrPartial++;
                    }
                }
                else if (string.Equals(entry.Category, "negative", StringComparison.Ordinal))
                {
                    await VerifyNegativeWireFixtureAsync(host, fixturesRoot, entry);
                    negativeRejected++;
                }
                else if (string.Equals(entry.Category, "serializer", StringComparison.Ordinal))
                {
                    bool verified = await VerifySerializerFragmentAsync(fixturesRoot, entry);
                    if (verified)
                    {
                        serializerPassed++;
                    }
                    else
                    {
                        serializerPendingOrPartial++;
                    }
                }
                else if (string.Equals(entry.Category, "wsdl", StringComparison.Ordinal))
                {
                    await VerifyWsdlFixtureAsync(host, fixturesRoot, entry);
                    wsdlPassed++;
                }
                else
                {
                    unexpectedFailures++;
                }
            }

            _output.WriteLine(
                $"CoreWCF 1.9.2 Corpus Results (Total={manifest.Entries.Count}): " +
                $"PositivePassed={positivePassed}, PositivePendingOrPartial={positivePendingOrPartial}, " +
                $"NegativeExcludedRejected={negativeRejected}, SerializerPassed={serializerPassed}, " +
                $"SerializerPendingOrPartial={serializerPendingOrPartial}, WsdlPassed={wsdlPassed}, " +
                $"UnexpectedFailures={unexpectedFailures}.");

            Assert.Equal(0, unexpectedFailures);
            Assert.Equal(16, positivePassed);
            Assert.Equal(4, positivePendingOrPartial);
            Assert.Equal(27, negativeRejected);
            Assert.Equal(10, serializerPassed);
            Assert.Equal(2, serializerPendingOrPartial);
            Assert.Equal(4, wsdlPassed);
        }

        #endregion

        #region Private Methods

        private static bool AreXmlInfosetsEquivalent(string leftXml, string rightXml)
        {
            XDocument left = ParseStrictXDocument(leftXml);
            XDocument right = ParseStrictXDocument(rightXml);

            return AreElementsEquivalent(left.Root!, right.Root!);
        }

        private static bool AreElementsEquivalent(XElement left, XElement right)
        {
            if (left.Name != right.Name)
            {
                return false;
            }

            List<XAttribute> leftAttrs = FilterSemanticAttributes(left);
            List<XAttribute> rightAttrs = FilterSemanticAttributes(right);
            if (leftAttrs.Count != rightAttrs.Count)
            {
                return false;
            }

            foreach (XAttribute attr in leftAttrs)
            {
                XAttribute? match = rightAttrs.Find(a => a.Name == attr.Name && string.Equals(a.Value, attr.Value, StringComparison.Ordinal));
                if (match is null)
                {
                    return false;
                }
            }

            List<XElement> leftChildren = [.. left.Elements()];
            List<XElement> rightChildren = [.. right.Elements()];
            if (leftChildren.Count != rightChildren.Count)
            {
                return false;
            }

            if (leftChildren.Count == 0)
            {
                return string.Equals(left.Value, right.Value, StringComparison.Ordinal);
            }

            for (int index = 0; index < leftChildren.Count; index++)
            {
                if (!AreElementsEquivalent(leftChildren[index], rightChildren[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static List<XAttribute> FilterSemanticAttributes(XElement element)
        {
            List<XAttribute> attrs = new List<XAttribute>();
            foreach (XAttribute attr in element.Attributes())
            {
                if (attr.IsNamespaceDeclaration)
                {
                    continue;
                }

                attrs.Add(attr);
            }

            return attrs;
        }

        private static XDocument ParseStrictXDocument(string xml)
        {
            XmlReaderSettings settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };
            using StringReader stringReader = new StringReader(xml);
            using XmlReader reader = XmlReader.Create(stringReader, settings);

            return XDocument.Load(reader, LoadOptions.None);
        }

        private static async Task<FixtureMeta> ReadMetaAsync(string fixturesRoot, ManifestEntry entry)
        {
            string metaRelative = entry.Files.Find(static f => f.EndsWith(".meta.json", StringComparison.Ordinal))!;
            string metaPath = Path.Combine(fixturesRoot, metaRelative.Replace('/', Path.DirectorySeparatorChar));
            string json = await File.ReadAllTextAsync(metaPath, TestContext.Current.CancellationToken);

            return JsonSerializer.Deserialize<FixtureMeta>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        }

        private static async Task<HttpResponseMessage> SendRawSoapRequestAsync(
            HttpClient client,
            FixtureMeta meta,
            byte[] requestBytes,
            string? pathOverride = null)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, pathOverride ?? meta.Path);
            if (!string.IsNullOrWhiteSpace(meta.RequestSoapAction))
            {
                request.Headers.TryAddWithoutValidation("SOAPAction", meta.RequestSoapAction);
            }

            request.Content = new ByteArrayContent(requestBytes);
            if (!string.IsNullOrWhiteSpace(meta.RequestContentType))
            {
                request.Content.Headers.TryAddWithoutValidation("Content-Type", meta.RequestContentType);
            }

            return await client.SendAsync(request, TestContext.Current.CancellationToken);
        }

        private static async Task VerifyNegativeWireFixtureAsync(
            CompatibilityServerHost host,
            string fixturesRoot,
            ManifestEntry entry)
        {
            host.ResetInvocationCount();

            FixtureMeta meta = await ReadMetaAsync(fixturesRoot, entry);
            string requestRelative = entry.Files.Find(static f => f.EndsWith(".request.xml", StringComparison.Ordinal) || f.EndsWith(".request.bin", StringComparison.Ordinal))!;
            byte[] requestBytes = await File.ReadAllBytesAsync(
                Path.Combine(fixturesRoot, requestRelative.Replace('/', Path.DirectorySeparatorChar)),
                TestContext.Current.CancellationToken);

            string? pathOverride = string.Equals(entry.FixtureId, "NEG-18D", StringComparison.Ordinal)
                ? "/soap11/echo-dcs-header-quota.svc"
                : null;

            using HttpResponseMessage response = await SendRawSoapRequestAsync(host.HttpClient, meta, requestBytes, pathOverride);

            Assert.Equal(0, host.ServiceInvocations);

            switch (entry.FixtureId)
            {
                case "NEG-01":
                case "NEG-02":
                case "NEG-03A":
                case "NEG-03B":
                case "NEG-10":
                case "NEG-11":
                case "NEG-14A":
                case "NEG-14B":
                case "NEG-15":
                case "NEG-16A":
                case "NEG-16B":
                case "NEG-18B":
                    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                    break;

                case "NEG-18A":
                case "NEG-19":
                    Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
                    Assert.Equal(
                        "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault><faultcode>s:Client</faultcode><faultstring>SOAP request payload failed schema or type validation.</faultstring></s:Fault></s:Body></s:Envelope>",
                        await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
                    break;

                case "NEG-04":
                case "NEG-05":
                case "NEG-07":
                case "NEG-12":
                case "NEG-17A":
                case "NEG-17B":
                case "NEG-18C":
                    Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
                    XDocument clientFaultDoc = ParseStrictXDocument(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
                    Assert.Equal(
                        "s:Client",
                        clientFaultDoc.Root!
                            .Element(XName.Get("Body", "http://schemas.xmlsoap.org/soap/envelope/"))!
                            .Element(XName.Get("Fault", "http://schemas.xmlsoap.org/soap/envelope/"))!
                            .Element("faultcode")!
                            .Value);
                    break;

                case "NEG-06A":
                    Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
                    Assert.Equal(
                        "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault><faultcode>s:MustUnderstand</faultcode><faultstring>One or more mandatory SOAP headers were not understood.</faultstring></s:Fault></s:Body></s:Envelope>",
                        await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
                    break;

                case "NEG-06B":
                    Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
                    string neg06bBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
                    Assert.Equal(
                        "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Header><s:NotUnderstood xmlns:h0=\"urn:unknown\" qname=\"h0:UnknownMandatory\"/></s:Header><s:Body><s:Fault><s:Code><s:Value>s:MustUnderstand</s:Value></s:Code><s:Reason><s:Text xml:lang=\"en-US\">One or more mandatory SOAP headers were not understood.</s:Text></s:Reason></s:Fault></s:Body></s:Envelope>",
                        neg06bBody);
                    break;

                case "NEG-08":
                    Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
                    break;

                case "NEG-09":
                    Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
                    break;

                case "NEG-13":
                    Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
                    Assert.Equal(
                        "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault><faultcode>s:VersionMismatch</faultcode><faultstring>SOAP Envelope namespace does not match the endpoint SOAP version.</faultstring></s:Fault></s:Body></s:Envelope>",
                        await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
                    break;

                case "NEG-18D":
                    Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
                    Assert.Equal(
                        "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault><faultcode>s:Client</faultcode><faultstring>SOAP headers exceeded the configured byte budget.</faultstring></s:Fault></s:Body></s:Envelope>",
                        await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
                    break;

                default:
                    throw new InvalidOperationException($"Unhandled negative fixture ID: {entry.FixtureId}");
            }
        }

        private async Task<bool> VerifyPositiveWireFixtureAsync(
            CompatibilityServerHost host,
            string fixturesRoot,
            ManifestEntry entry)
        {
            if (string.Equals(entry.FixtureId, "SOAP11-TRANSFER-DCS-DEFAULT", StringComparison.Ordinal))
            {
                return false;
            }

            host.ResetInvocationCount();
            FixtureMeta meta = await ReadMetaAsync(fixturesRoot, entry);
            string requestRelative = entry.Files.Find(static f => f.EndsWith(".request.xml", StringComparison.Ordinal))!;
            byte[] requestBytes = await File.ReadAllBytesAsync(
                Path.Combine(fixturesRoot, requestRelative.Replace('/', Path.DirectorySeparatorChar)),
                TestContext.Current.CancellationToken);

            if (string.Equals(entry.FixtureId, "SOAP11-TRANSFER-FAULT-RECEIVER", StringComparison.Ordinal))
            {
                using HttpResponseMessage faultRes = await SendRawSoapRequestAsync(host.HttpClient, meta, requestBytes);
                Assert.Equal(HttpStatusCode.InternalServerError, faultRes.StatusCode);
                Assert.Equal("text/xml; charset=utf-8", faultRes.Content.Headers.ContentType?.ToString());
                string faultXml = await faultRes.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault><faultcode>s:Server</faultcode><faultstring>An internal server error occurred while processing the SOAP request.</faultstring></s:Fault></s:Body></s:Envelope>",
                    faultXml);

                return true;
            }

            string? pathOverride = string.Equals(entry.FixtureId, "NEG-18D", StringComparison.Ordinal)
                ? "/soap11/echo-dcs-header-quota.svc"
                : null;

            using HttpResponseMessage response = await SendRawSoapRequestAsync(host.HttpClient, meta, requestBytes, pathOverride);
            string actualBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            if (string.Equals(entry.FixtureId, "SOAP11-TRANSFER-FAULT-TYPED", StringComparison.Ordinal))
            {
                Assert.Equal(1, host.ServiceInvocations);
                Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault><faultcode>s:Client</faultcode><faultstring>Payment validation failed.</faultstring></s:Fault></s:Body></s:Envelope>",
                    actualBody);

                return false;
            }

            if (string.Equals(entry.FixtureId, "SOAP12-TRANSFER-FAULT-TYPED", StringComparison.Ordinal))
            {
                Assert.Equal(1, host.ServiceInvocations);
                Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body><s:Fault><s:Code><s:Value>s:Sender</s:Value></s:Code><s:Reason><s:Text xml:lang=\"en-US\">Payment validation failed.</s:Text></s:Reason></s:Fault></s:Body></s:Envelope>",
                    actualBody);

                return false;
            }

            if (string.Equals(entry.FixtureId, "SOAP11-ECHO-HEADER", StringComparison.Ordinal))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(1, host.ServiceInvocations);
                Assert.Single(host.ApiKeyHeaderHandler.Headers);
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><EchoHeaderResponse xmlns=\"http://tempuri.org/\"><SayHello>Saying Hello Message Hello</SayHello></EchoHeaderResponse></s:Body></s:Envelope>",
                    actualBody);

                return false;
            }

            if (string.Equals(entry.FixtureId, "SOAP11-HEADER-MUSTUNDERSTAND-OTHER-ACTOR", StringComparison.Ordinal))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(1, host.ServiceInvocations);
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><EchoStringResponse xmlns=\"http://tempuri.org/\"><EchoStringResult>hello</EchoStringResult></EchoStringResponse></s:Body></s:Envelope>",
                    actualBody);

                return true;
            }

            if (string.Equals(entry.FixtureId, "SOAP12-HEADER-MUSTUNDERSTAND-ROLE-NONE", StringComparison.Ordinal))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(1, host.ServiceInvocations);
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body><EchoStringResponse xmlns=\"http://tempuri.org/\"><EchoStringResult>hello</EchoStringResult></EchoStringResponse></s:Body></s:Envelope>",
                    actualBody);

                return true;
            }

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(meta.ResponseContentType, response.Content.Headers.ContentType?.ToString());
            Assert.Equal(1, host.ServiceInvocations);

            string responseRelative = entry.Files.Find(static f => f.EndsWith(".response.xml", StringComparison.Ordinal))!;
            string expectedXml = await File.ReadAllTextAsync(
                Path.Combine(fixturesRoot, responseRelative.Replace('/', Path.DirectorySeparatorChar)),
                TestContext.Current.CancellationToken);

            Assert.True(
                AreXmlInfosetsEquivalent(expectedXml, actualBody),
                $"XML infoset mismatch for fixture {entry.FixtureId}.\nExpected: {expectedXml}\nActual: {actualBody}");

            return true;
        }

        private static async Task<bool> VerifySerializerFragmentAsync(string fixturesRoot, ManifestEntry entry)
        {
            if (string.Equals(entry.FixtureId, "SER-DCS-TRANSFER-DEFAULT", StringComparison.Ordinal) ||
                string.Equals(entry.FixtureId, "SER-DCS-PAYMENT-FAULT", StringComparison.Ordinal))
            {
                return false;
            }

            string fragmentPath = Path.Combine(fixturesRoot, entry.Files[0].Replace('/', Path.DirectorySeparatorChar));
            byte[] xmlBytes = await File.ReadAllBytesAsync(fragmentPath, TestContext.Current.CancellationToken);

            CompatibilityXmlContext context = new CompatibilityXmlContext(new XmlSerializerOptions
            {
                AllowDuplicateProperties = false,
                OmitXmlDeclaration = true,
                UnmappedMemberHandling = XmlUnmappedMemberHandling.Disallow
            });

            switch (entry.FixtureId)
            {
                case "SER-DCS-ECHO":
                    DcsEchoStringPayload? dcsEcho = XmlSerializer.Deserialize(xmlBytes.AsSpan(), context.DcsEchoStringPayload);
                    Assert.NotNull(dcsEcho);
                    Assert.Equal("hello", dcsEcho.Value);
                    byte[] dcsEchoRoundTrip = XmlSerializer.SerializeToUtf8Bytes(dcsEcho, context.DcsEchoStringPayload);
                    Assert.Equal(
                        "<string xmlns=\"http://schemas.microsoft.com/2003/10/Serialization/\">hello</string>",
                        Encoding.UTF8.GetString(dcsEchoRoundTrip));
                    return true;

                case "SER-XML-ECHO":
                    XmlSerEchoStringPayload? xmlEcho = XmlSerializer.Deserialize(xmlBytes.AsSpan(), context.XmlSerEchoStringPayload);
                    Assert.NotNull(xmlEcho);
                    Assert.Equal("hello", xmlEcho.Value);
                    byte[] xmlEchoRoundTrip = XmlSerializer.SerializeToUtf8Bytes(xmlEcho, context.XmlSerEchoStringPayload);
                    Assert.Equal(
                        "<string>hello</string>",
                        Encoding.UTF8.GetString(xmlEchoRoundTrip));
                    return true;

                case "SER-DCS-TRANSFER-URN":
                case "SER-XML-TRANSFER-URN":
                    TransferRequest? transfer = XmlSerializer.Deserialize(xmlBytes.AsSpan(), context.TransferRequest);
                    Assert.NotNull(transfer);
                    Assert.Equal("123456", transfer.AccountNumber);
                    Assert.Equal(100.50m, transfer.Amount);
                    Assert.NotNull(transfer.Metadata);
                    Assert.Equal("CORR-2026-0001", transfer.Metadata.CorrelationId);
                    return true;

                case "SER-DCS-TRANSFER-NIL":
                case "SER-XML-TRANSFER-NIL":
                    TransferRequest? nilTransfer = XmlSerializer.Deserialize(xmlBytes.AsSpan(), context.TransferRequest);
                    Assert.NotNull(nilTransfer);
                    Assert.Null(nilTransfer.Metadata);
                    Assert.Null(nilTransfer.Note);
                    return true;

                case "SER-DCS-TRANSFER-RESULT":
                case "SER-XML-TRANSFER-RESULT":
                    TransferResult? result = XmlSerializer.Deserialize(xmlBytes.AsSpan(), context.TransferResult);
                    Assert.NotNull(result);
                    Assert.Equal("TX-001", result.TransactionId);
                    Assert.Equal(PaymentStatus.Completed, result.Status);
                    return true;

                case "SER-DCS-BATCH-TRANSFER":
                case "SER-XML-BATCH-TRANSFER":
                    BatchTransferRequest? batch = XmlSerializer.Deserialize(xmlBytes.AsSpan(), context.BatchTransferRequest);
                    Assert.NotNull(batch);
                    Assert.Equal("BATCH-01", batch.BatchId);
                    Assert.Equal(2, batch.Items.Count);
                    return true;

                default:
                    return false;
            }
        }

        private static async Task VerifyWsdlFixtureAsync(
            CompatibilityServerHost host,
            string fixturesRoot,
            ManifestEntry entry)
        {
            switch (entry.FixtureId)
            {
                case "WSDL-ECHO-SOAP11":
                    await AssertMetadataBytesEqualAsync(host.HttpClient, "/wsdl/echo-soap11.svc?wsdl", Path.Combine(fixturesRoot, "wsdl", "echo-soap11.wsdl"));
                    await AssertMetadataBytesEqualAsync(host.HttpClient, "/wsdl/echo-soap11.svc?xsd=xsd0", Path.Combine(fixturesRoot, "wsdl", "echo-soap11.xsd0.xsd"));
                    await AssertMetadataBytesEqualAsync(host.HttpClient, "/wsdl/echo-soap11.svc?xsd=xsd1", Path.Combine(fixturesRoot, "wsdl", "echo-soap11.xsd1.xsd"));
                    break;

                case "WSDL-PAYMENT-DCS-SOAP11":
                    await AssertMetadataBytesEqualAsync(host.HttpClient, "/wsdl/payment-dcs-soap11.svc?wsdl", Path.Combine(fixturesRoot, "wsdl", "payment-dcs-soap11.single.wsdl"));
                    await AssertMetadataBytesEqualAsync(host.HttpClient, "/wsdl/payment-dcs-soap11.svc?xsd=xsd0", Path.Combine(fixturesRoot, "wsdl", "payment-dcs-soap11.xsd0.xsd"));
                    await AssertMetadataBytesEqualAsync(host.HttpClient, "/wsdl/payment-dcs-soap11.svc?xsd=xsd1", Path.Combine(fixturesRoot, "wsdl", "payment-dcs-soap11.xsd1.xsd"));
                    break;

                case "WSDL-PAYMENT-XMLSER-SOAP11":
                    await AssertMetadataBytesEqualAsync(host.HttpClient, "/wsdl/payment-xmlser-soap11.svc?wsdl", Path.Combine(fixturesRoot, "wsdl", "payment-xmlser-soap11.single.wsdl"));
                    break;

                case "WSDL-PAYMENT-DCS-SOAP12":
                    await AssertMetadataBytesEqualAsync(host.HttpClient, "/wsdl/payment-dcs-soap12.svc?wsdl", Path.Combine(fixturesRoot, "wsdl", "payment-dcs-soap12.single.wsdl"));
                    break;

                default:
                    throw new InvalidOperationException($"Unhandled WSDL fixture ID: {entry.FixtureId}");
            }

            foreach (string relativeFile in entry.Files)
            {
                string fullPath = Path.Combine(fixturesRoot, relativeFile.Replace('/', Path.DirectorySeparatorChar));
                string xml = await File.ReadAllTextAsync(fullPath, TestContext.Current.CancellationToken);
                XDocument doc = ParseStrictXDocument(xml);
                Assert.NotNull(doc.Root);
            }
        }

        private static async Task AssertMetadataBytesEqualAsync(HttpClient client, string requestUri, string expectedFilePath)
        {
            byte[] expectedBytes = await File.ReadAllBytesAsync(expectedFilePath, TestContext.Current.CancellationToken);
            using HttpResponseMessage response = await client.GetAsync(requestUri, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            byte[] actualBytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
            Assert.True(expectedBytes.AsSpan().SequenceEqual(actualBytes), $"Metadata byte mismatch for {requestUri}");
        }

        #endregion

        #region Nested Types

        private sealed class FixtureMeta
        {

            #region Properties

            public string? Path { get; set; }

            public string? RequestContentType { get; set; }

            public string? RequestSoapAction { get; set; }

            public string? ResponseContentType { get; set; }

            public int ResponseStatusCode { get; set; }

            #endregion

        }

        private sealed class ManifestDocument
        {

            #region Properties

            public List<ManifestEntry> Entries { get; set; } = [];

            #endregion

        }

        private sealed class ManifestEntry
        {

            #region Properties

            public string Category { get; set; } = string.Empty;

            public string ExpectedOutcome { get; set; } = string.Empty;

            public List<string> Files { get; set; } = [];

            public string FixtureId { get; set; } = string.Empty;

            public Dictionary<string, string> Sha256ByFile { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

            #endregion

        }

        #endregion

    }

}
