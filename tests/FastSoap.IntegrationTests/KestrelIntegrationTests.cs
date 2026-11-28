using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using System.Xml;
using System.Xml.Linq;

using CloudNimble.Text.Xml;
using CloudNimble.Text.Xml.Serialization;

using FastSoap.Abstractions;
using FastSoap.AspNetCore;
using FastSoap.Metadata;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Xunit;

namespace FastSoap.IntegrationTests
{

    /// <summary>
    /// End-to-end real Kestrel integration tests over loopback TCP (<c>127.0.0.1:0</c>) verifying
    /// SOAP 1.1/1.2 dispatch, generator parity, auth, quotas, security rejections, metadata, abort, and telemetry.
    /// </summary>
    public sealed class KestrelIntegrationTests
    {

        #region Fields

        private static readonly SoapOperationDescriptor EchoDescriptor = new SoapOperationDescriptor(
            "Echo",
            "urn:echo/IEchoService/Echo",
            "urn:echo/IEchoService/EchoResponse",
            new SoapQName("urn:echo", "Echo"),
            new SoapQName("urn:echo", "EchoResponse"));

        #endregion

        #region Public Methods

        [Fact]
        public async Task Authorization_RequiresBearerAndPolicy_WithZeroInvocationsOn401And403()
        {
            await using KestrelHostHarness harness = await KestrelHostHarness.StartAsync();

            byte[] validTransferSoap11 = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><Transfer xmlns=\"urn:payments\"><request><AccountNumber>100200</AccountNumber><Amount>50.00</Amount><Currency>USD</Currency></request></Transfer></s:Body></s:Envelope>"u8.ToArray();

            using (HttpRequestMessage unauthReq = CreateSoap11Request("/secure/PaymentService.svc", "urn:payments/IPaymentService/Transfer", validTransferSoap11))
            using (HttpResponseMessage unauthRes = await harness.Client.SendAsync(unauthReq, TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.Unauthorized, unauthRes.StatusCode);
                Assert.Equal(0, harness.Counter.TransferCount);
            }

            using (HttpRequestMessage forbidReq = CreateSoap11Request("/secure/PaymentService.svc", "urn:payments/IPaymentService/Transfer", validTransferSoap11))
            {
                forbidReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "readonly-user");
                using HttpResponseMessage forbidRes = await harness.Client.SendAsync(forbidReq, TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.Forbidden, forbidRes.StatusCode);
                Assert.Equal(0, harness.Counter.TransferCount);
            }

            using (HttpRequestMessage okReq = CreateSoap11Request("/secure/PaymentService.svc", "urn:payments/IPaymentService/Transfer", validTransferSoap11))
            {
                okReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "valid-payment-user");
                using HttpResponseMessage okRes = await harness.Client.SendAsync(okReq, TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.OK, okRes.StatusCode);
                Assert.Equal("text/xml; charset=utf-8", okRes.Content.Headers.ContentType?.ToString());
                Assert.Equal(1, harness.Counter.TransferCount);
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><s:Body><TransferResponse xmlns=\"urn:payments\"><TransferResult><TransactionId>TX-100200</TransactionId><Successful>true</Successful><ProcessedAtUtc>2026-11-26T12:00:00Z</ProcessedAtUtc><Status>Completed</Status></TransferResult></TransferResponse></s:Body></s:Envelope>",
                    await okRes.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            }
        }

        [Fact]
        public async Task ManualAndGeneratedEndpoints_ProduceByteIdenticalSoap11AndSoap12Responses()
        {
            await using KestrelHostHarness harness = await KestrelHostHarness.StartAsync();

            byte[] transferRequest = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><Transfer xmlns=\"urn:payments\"><request xmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\"><AccountNumber>123456</AccountNumber><Amount>100.50</Amount><Currency>USD</Currency><Metadata><CorrelationId>CORR-1</CorrelationId><Reference>REF-1</Reference></Metadata><Note>Wire parity</Note></request></Transfer></s:Body></s:Envelope>"u8.ToArray();

            using HttpRequestMessage manual11Req = CreateSoap11Request("/manual/PaymentService.svc", "urn:payments/IPaymentService/Transfer", transferRequest);
            using HttpRequestMessage gen11Req = CreateSoap11Request("/generated/PaymentService.svc", "urn:payments/IPaymentService/Transfer", transferRequest);

            using HttpResponseMessage manual11Res = await harness.Client.SendAsync(manual11Req, TestContext.Current.CancellationToken);
            using HttpResponseMessage gen11Res = await harness.Client.SendAsync(gen11Req, TestContext.Current.CancellationToken);

            byte[] manual11Bytes = await manual11Res.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
            byte[] gen11Bytes = await gen11Res.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, manual11Res.StatusCode);
            Assert.Equal(HttpStatusCode.OK, gen11Res.StatusCode);
            Assert.Equal("text/xml; charset=utf-8", manual11Res.Content.Headers.ContentType?.ToString());
            Assert.Equal("text/xml; charset=utf-8", gen11Res.Content.Headers.ContentType?.ToString());
            Assert.Equal(manual11Bytes.Length, manual11Res.Content.Headers.ContentLength);
            Assert.Equal(gen11Bytes.Length, gen11Res.Content.Headers.ContentLength);
            Assert.True(manual11Bytes.AsSpan().SequenceEqual(gen11Bytes));
            Assert.Equal(
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><s:Body><TransferResponse xmlns=\"urn:payments\"><TransferResult><TransactionId>TX-123456</TransactionId><Successful>true</Successful><ProcessedAtUtc>2026-11-26T12:00:00Z</ProcessedAtUtc><Status>Completed</Status></TransferResult></TransferResponse></s:Body></s:Envelope>",
                Encoding.UTF8.GetString(manual11Bytes));

            byte[] transferRequest12 = "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body><Transfer xmlns=\"urn:payments\"><request xmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\"><AccountNumber>789012</AccountNumber><Amount>42.00</Amount><Currency>EUR</Currency><Metadata i:nil=\"true\"/><Note i:nil=\"true\"/></request></Transfer></s:Body></s:Envelope>"u8.ToArray();

            using HttpRequestMessage manual12Req = CreateSoap12Request("/manual/PaymentService.svc", "urn:payments/IPaymentService/Transfer", transferRequest12);
            using HttpRequestMessage gen12Req = CreateSoap12Request("/generated/PaymentService.svc", "urn:payments/IPaymentService/Transfer", transferRequest12);

            using HttpResponseMessage manual12Res = await harness.Client.SendAsync(manual12Req, TestContext.Current.CancellationToken);
            using HttpResponseMessage gen12Res = await harness.Client.SendAsync(gen12Req, TestContext.Current.CancellationToken);

            byte[] manual12Bytes = await manual12Res.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
            byte[] gen12Bytes = await gen12Res.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, manual12Res.StatusCode);
            Assert.Equal(HttpStatusCode.OK, gen12Res.StatusCode);
            Assert.Equal("application/soap+xml; charset=utf-8", manual12Res.Content.Headers.ContentType?.ToString());
            Assert.Equal("application/soap+xml; charset=utf-8", gen12Res.Content.Headers.ContentType?.ToString());
            Assert.True(manual12Bytes.AsSpan().SequenceEqual(gen12Bytes));
            Assert.Equal(
                "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><s:Body><TransferResponse xmlns=\"urn:payments\"><TransferResult><TransactionId>TX-789012</TransactionId><Successful>true</Successful><ProcessedAtUtc>2026-11-26T12:00:00Z</ProcessedAtUtc><Status>Completed</Status></TransferResult></TransferResponse></s:Body></s:Envelope>",
                Encoding.UTF8.GetString(manual12Bytes));

            Assert.Equal(4, harness.Counter.TransferCount);
        }

        [Fact]
        public async Task ManualAndGeneratedEndpoints_RejectDuplicateAndUnmappedMembersWithExactFaultParity()
        {
            await using KestrelHostHarness harness = await KestrelHostHarness.StartAsync();

            byte[] duplicateSoap11 = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><Transfer xmlns=\"urn:payments\"><request><AccountNumber>123456</AccountNumber><AccountNumber>999999</AccountNumber><Amount>100.50</Amount><Currency>USD</Currency></request></Transfer></s:Body></s:Envelope>"u8.ToArray();
            byte[] unmappedSoap11 = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><Transfer xmlns=\"urn:payments\"><request><AccountNumber>123456</AccountNumber><Amount>100.50</Amount><Currency>USD</Currency><Unexpected>evil</Unexpected></request></Transfer></s:Body></s:Envelope>"u8.ToArray();
            byte[] duplicateSoap12 = "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body><Transfer xmlns=\"urn:payments\"><request><AccountNumber>123456</AccountNumber><AccountNumber>999999</AccountNumber><Amount>100.50</Amount><Currency>USD</Currency></request></Transfer></s:Body></s:Envelope>"u8.ToArray();
            byte[] unmappedSoap12 = "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body><Transfer xmlns=\"urn:payments\"><request><AccountNumber>123456</AccountNumber><Amount>100.50</Amount><Currency>USD</Currency><Unexpected>evil</Unexpected></request></Transfer></s:Body></s:Envelope>"u8.ToArray();

            const string expectedSoap11Fault = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault><faultcode>s:Client</faultcode><faultstring>SOAP request payload failed schema or type validation.</faultstring></s:Fault></s:Body></s:Envelope>";
            const string expectedSoap12Fault = "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body><s:Fault><s:Code><s:Value>s:Sender</s:Value></s:Code><s:Reason><s:Text xml:lang=\"en-US\">SOAP request payload failed schema or type validation.</s:Text></s:Reason></s:Fault></s:Body></s:Envelope>";

            foreach (byte[] payload in new[] { duplicateSoap11, unmappedSoap11 })
            {
                using HttpRequestMessage manualReq = CreateSoap11Request("/manual/PaymentService.svc", "urn:payments/IPaymentService/Transfer", payload);
                using HttpRequestMessage genReq = CreateSoap11Request("/generated/PaymentService.svc", "urn:payments/IPaymentService/Transfer", payload);

                using HttpResponseMessage manualRes = await harness.Client.SendAsync(manualReq, TestContext.Current.CancellationToken);
                using HttpResponseMessage genRes = await harness.Client.SendAsync(genReq, TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.InternalServerError, manualRes.StatusCode);
                Assert.Equal(HttpStatusCode.InternalServerError, genRes.StatusCode);
                Assert.Equal("text/xml; charset=utf-8", manualRes.Content.Headers.ContentType?.ToString());
                Assert.Equal("text/xml; charset=utf-8", genRes.Content.Headers.ContentType?.ToString());
                Assert.Equal(expectedSoap11Fault, await manualRes.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
                Assert.Equal(expectedSoap11Fault, await genRes.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            }

            foreach (byte[] payload in new[] { duplicateSoap12, unmappedSoap12 })
            {
                using HttpRequestMessage manualReq = CreateSoap12Request("/manual/PaymentService.svc", "urn:payments/IPaymentService/Transfer", payload);
                using HttpRequestMessage genReq = CreateSoap12Request("/generated/PaymentService.svc", "urn:payments/IPaymentService/Transfer", payload);

                using HttpResponseMessage manualRes = await harness.Client.SendAsync(manualReq, TestContext.Current.CancellationToken);
                using HttpResponseMessage genRes = await harness.Client.SendAsync(genReq, TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.BadRequest, manualRes.StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, genRes.StatusCode);
                Assert.Equal("application/soap+xml; charset=utf-8", manualRes.Content.Headers.ContentType?.ToString());
                Assert.Equal("application/soap+xml; charset=utf-8", genRes.Content.Headers.ContentType?.ToString());
                Assert.Equal(expectedSoap12Fault, await manualRes.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
                Assert.Equal(expectedSoap12Fault, await genRes.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            }

            using (HttpRequestMessage soap11OnSoap12OnlyReq = CreateSoap11Request("/configured/PaymentService.svc", "urn:payments/IPaymentService/Transfer", duplicateSoap11))
            using (HttpResponseMessage soap11OnSoap12OnlyRes = await harness.Client.SendAsync(soap11OnSoap12OnlyReq, TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.UnsupportedMediaType, soap11OnSoap12OnlyRes.StatusCode);
            }

            using (HttpResponseMessage wsdlRes = await harness.Client.GetAsync("/configured/PaymentService.svc?wsdl", TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, wsdlRes.StatusCode);
            }

            Assert.Equal(0, harness.Counter.TransferCount);
        }

        [Fact]
        public async Task MetadataEndpoints_ServeWsdlAndXsdWithETag304_AndRejectUntrustedHostHeaders()
        {
            await using KestrelHostHarness harness = await KestrelHostHarness.StartAsync();

            using (HttpResponseMessage disabledRes = await harness.Client.GetAsync("/generated/PaymentService.svc?wsdl", TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.MethodNotAllowed, disabledRes.StatusCode);
            }

            string wsdlBody;
            string wsdlEtag;
            using (HttpRequestMessage wsdlReq = new HttpRequestMessage(HttpMethod.Get, "/manual/PaymentService.svc?wsdl"))
            {
                wsdlReq.Headers.Host = "evil.attacker.example:9999";
                wsdlReq.Headers.TryAddWithoutValidation("X-Forwarded-Host", "phishing.attacker.example");

                using HttpResponseMessage wsdlRes = await harness.Client.SendAsync(wsdlReq, TestContext.Current.CancellationToken);
                Assert.Equal(HttpStatusCode.OK, wsdlRes.StatusCode);
                Assert.Equal("text/xml; charset=utf-8", wsdlRes.Content.Headers.ContentType?.ToString());
                Assert.NotNull(wsdlRes.Headers.ETag);

                wsdlEtag = wsdlRes.Headers.ETag.Tag;
                wsdlBody = await wsdlRes.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

                Assert.DoesNotContain("evil.attacker.example", wsdlBody, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("phishing.attacker.example", wsdlBody, StringComparison.OrdinalIgnoreCase);

                XDocument wsdlDoc = ParseStrictXDocument(wsdlBody);
                XNamespace wsdlNs = "http://schemas.xmlsoap.org/wsdl/";
                XNamespace soap11Ns = "http://schemas.xmlsoap.org/wsdl/soap/";
                XNamespace soap12Ns = "http://schemas.xmlsoap.org/wsdl/soap12/";
                XNamespace xsNs = "http://www.w3.org/2001/XMLSchema";

                XElement serviceEl = wsdlDoc.Root!.Element(wsdlNs + "service")!;
                List<XElement> ports = [.. serviceEl.Elements(wsdlNs + "port")];
                Assert.Equal(2, ports.Count);
                Assert.Equal(
                    "https://payments.example.com/manual/PaymentService.svc",
                    (string?)ports[0].Element(soap11Ns + "address")!.Attribute("location"));
                Assert.Equal(
                    "https://payments.example.com/manual/PaymentService.svc",
                    (string?)ports[1].Element(soap12Ns + "address")!.Attribute("location"));

                XElement importEl = wsdlDoc.Root!
                    .Element(wsdlNs + "types")!
                    .Element(xsNs + "schema")!
                    .Element(xsNs + "import")!;
                Assert.Equal(
                    "https://payments.example.com/manual/PaymentService.svc?xsd=xsd0",
                    (string?)importEl.Attribute("schemaLocation"));
            }

            using (HttpRequestMessage notModifiedReq = new HttpRequestMessage(HttpMethod.Get, "/manual/PaymentService.svc?wsdl"))
            {
                notModifiedReq.Headers.IfNoneMatch.ParseAdd(wsdlEtag);
                using HttpResponseMessage notModifiedRes = await harness.Client.SendAsync(notModifiedReq, TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.NotModified, notModifiedRes.StatusCode);
                byte[] notModifiedBody = await notModifiedRes.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
                Assert.Empty(notModifiedBody);
            }

            using (HttpResponseMessage xsdRes = await harness.Client.GetAsync("/manual/PaymentService.svc?xsd=xsd0", TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, xsdRes.StatusCode);
                Assert.Equal("text/xml; charset=utf-8", xsdRes.Content.Headers.ContentType?.ToString());
                Assert.NotNull(xsdRes.Headers.ETag);

                string xsdBody = await xsdRes.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
                XDocument xsdDoc = ParseStrictXDocument(xsdBody);
                Assert.Equal("urn:payments", (string?)xsdDoc.Root!.Attribute("targetNamespace"));
            }

            using (HttpResponseMessage traversalRes = await harness.Client.GetAsync("/manual/PaymentService.svc?xsd=../xsd0", TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.BadRequest, traversalRes.StatusCode);
            }

            using (HttpResponseMessage unknownRes = await harness.Client.GetAsync("/manual/PaymentService.svc?xsd=missing", TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.NotFound, unknownRes.StatusCode);
            }
        }

        [Fact]
        public async Task NegativeAndAdversarialRequests_InvokeServiceZeroTimes_WithExactHttpStatusAndFaults()
        {
            await using KestrelHostHarness harness = await KestrelHostHarness.StartAsync();

            byte[] preEnvelopeMalformedXml = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\""u8.ToArray();
            using (HttpRequestMessage req = CreateSoap11Request("/manual/PaymentService.svc", "urn:echo/IEchoService/Echo", preEnvelopeMalformedXml))
            using (HttpResponseMessage res = await harness.Client.SendAsync(req, TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            }

            byte[] postEnvelopeMalformedPayload = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><Echo xmlns=\"urn:echo\"><Message>broken"u8.ToArray();
            using (HttpRequestMessage req = CreateSoap11Request("/manual/PaymentService.svc", "urn:echo/IEchoService/Echo", postEnvelopeMalformedPayload))
            using (HttpResponseMessage res = await harness.Client.SendAsync(req, TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault><faultcode>s:Client</faultcode><faultstring>SOAP request payload failed schema or type validation.</faultstring></s:Fault></s:Body></s:Envelope>",
                    await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            }

            byte[] dtdXxe = "<!DOCTYPE foo [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><Echo xmlns=\"urn:echo\"><Message>&xxe;</Message></Echo></s:Body></s:Envelope>"u8.ToArray();
            using (HttpRequestMessage req = CreateSoap11Request("/manual/PaymentService.svc", "urn:echo/IEchoService/Echo", dtdXxe))
            using (HttpResponseMessage res = await harness.Client.SendAsync(req, TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            }

            byte[] actionMismatch = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><Echo xmlns=\"urn:echo\"><Message>mismatch</Message></Echo></s:Body></s:Envelope>"u8.ToArray();
            using (HttpRequestMessage req = CreateSoap11Request("/manual/PaymentService.svc", "urn:payments/IPaymentService/Transfer", actionMismatch))
            using (HttpResponseMessage res = await harness.Client.SendAsync(req, TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);
                Assert.Equal("text/xml; charset=utf-8", res.Content.Headers.ContentType?.ToString());
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault><faultcode>s:Client</faultcode><faultstring>SOAP action does not match the Body element qualified name.</faultstring></s:Fault></s:Body></s:Envelope>",
                    await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            }

            string bigText = new string('A', 4096);
            byte[] oversizedKnownLength = Encoding.UTF8.GetBytes($"<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><Echo xmlns=\"urn:echo\"><Message>{bigText}</Message></Echo></s:Body></s:Envelope>");
            using (HttpRequestMessage req = CreateSoap11Request("/quota/PaymentService.svc", "urn:echo/IEchoService/Echo", oversizedKnownLength))
            using (HttpResponseMessage res = await harness.Client.SendAsync(req, TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.RequestEntityTooLarge, res.StatusCode);
            }

            using (HttpRequestMessage chunkedOversizedReq = new HttpRequestMessage(HttpMethod.Post, "/quota/PaymentService.svc"))
            {
                chunkedOversizedReq.Headers.TransferEncodingChunked = true;
                chunkedOversizedReq.Headers.TryAddWithoutValidation("SOAPAction", "\"urn:echo/IEchoService/Echo\"");
                chunkedOversizedReq.Content = new ChunkedBytesContent(oversizedKnownLength);

                using HttpResponseMessage res = await harness.Client.SendAsync(chunkedOversizedReq, TestContext.Current.CancellationToken);
                Assert.Equal(HttpStatusCode.RequestEntityTooLarge, res.StatusCode);
            }

            using (HttpRequestMessage jsonReq = new HttpRequestMessage(HttpMethod.Post, "/manual/PaymentService.svc"))
            {
                jsonReq.Headers.TryAddWithoutValidation("SOAPAction", "\"urn:echo/IEchoService/Echo\"");
                jsonReq.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                using HttpResponseMessage res = await harness.Client.SendAsync(jsonReq, TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.UnsupportedMediaType, res.StatusCode);
            }

            byte[] unknownMustUnderstand11 = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Header><x:RequireAuth xmlns:x=\"urn:unknown\" s:mustUnderstand=\"1\">true</x:RequireAuth></s:Header><s:Body><Echo xmlns=\"urn:echo\"><Message>x</Message></Echo></s:Body></s:Envelope>"u8.ToArray();
            using (HttpRequestMessage req = CreateSoap11Request("/manual/PaymentService.svc", "urn:echo/IEchoService/Echo", unknownMustUnderstand11))
            using (HttpResponseMessage res = await harness.Client.SendAsync(req, TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault><faultcode>s:MustUnderstand</faultcode><faultstring>One or more mandatory SOAP headers were not understood.</faultstring></s:Fault></s:Body></s:Envelope>",
                    await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            }

            byte[] unknownMustUnderstand12 = "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Header><x:RequireAuth xmlns:x=\"urn:unknown\" s:mustUnderstand=\"true\">true</x:RequireAuth></s:Header><s:Body><Echo xmlns=\"urn:echo\"><Message>x</Message></Echo></s:Body></s:Envelope>"u8.ToArray();
            using (HttpRequestMessage req = CreateSoap12Request("/manual/PaymentService.svc", "urn:echo/IEchoService/Echo", unknownMustUnderstand12))
            using (HttpResponseMessage res = await harness.Client.SendAsync(req, TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Header><s:NotUnderstood xmlns:h0=\"urn:unknown\" qname=\"h0:RequireAuth\"/></s:Header><s:Body><s:Fault><s:Code><s:Value>s:MustUnderstand</s:Value></s:Code><s:Reason><s:Text xml:lang=\"en-US\">One or more mandatory SOAP headers were not understood.</s:Text></s:Reason></s:Fault></s:Body></s:Envelope>",
                    await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            }

            Assert.Equal(0, harness.Counter.EchoCount);
            Assert.Equal(0, harness.Counter.TransferCount);
        }

        [Fact]
        public async Task RequestAborted_CancelsHandlerGracefully_WithoutWritingFault()
        {
            await using KestrelHostHarness harness = await KestrelHostHarness.StartAsync();

            byte[] slowBody = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><Echo xmlns=\"urn:echo\"><Message>slow</Message></Echo></s:Body></s:Envelope>"u8.ToArray();

            using CancellationTokenSource cts = new CancellationTokenSource();
            using HttpRequestMessage req = CreateSoap11Request("/slow/PaymentService.svc", "urn:echo/IEchoService/Echo", slowBody);

            Task<HttpResponseMessage> responseTask = harness.Client.SendAsync(req, cts.Token);
            bool entered = await harness.SlowHandlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(entered);

            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                using HttpResponseMessage _ = await responseTask.ConfigureAwait(false);
            });

            bool observedCancellation = await harness.SlowHandlerCanceled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(observedCancellation);
        }

        [Fact]
        public async Task Soap11AndSoap12Echo_WithHeaderHandlerAndTelemetry_EmitsExpectedTagsOnly()
        {
            List<Activity> recordedActivities = new List<Activity>();
            List<(string InstrumentName, Dictionary<string, object?> Tags)> recordedMetrics = new List<(string, Dictionary<string, object?>)>();
            Lock syncLock = new Lock();

            using ActivityListener activityListener = new ActivityListener
            {
                ShouldListenTo = static source => string.Equals(source.Name, FastSoapTelemetry.ActivitySourceName, StringComparison.Ordinal),
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    lock (syncLock)
                    {
                        recordedActivities.Add(activity);
                    }
                }
            };
            ActivitySource.AddActivityListener(activityListener);

            using MeterListener meterListener = new MeterListener();
            meterListener.InstrumentPublished = static (instrument, listener) =>
            {
                if (string.Equals(instrument.Meter.Name, FastSoapTelemetry.MeterName, StringComparison.Ordinal))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            meterListener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
            {
                Dictionary<string, object?> map = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, object?> kv in tags)
                {
                    map[kv.Key] = kv.Value;
                }

                lock (syncLock)
                {
                    recordedMetrics.Add((instrument.Name, map));
                }
            });
            meterListener.Start();

            await using KestrelHostHarness harness = await KestrelHostHarness.StartAsync();

            byte[] echoWithHeader = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Header><h:TraceId xmlns:h=\"urn:headers\" s:mustUnderstand=\"1\">SENSITIVE_PAYLOAD_TOKEN</h:TraceId></s:Header><s:Body><Echo xmlns=\"urn:echo\"><Message>SENSITIVE_ECHO_SECRET</Message></Echo></s:Body></s:Envelope>"u8.ToArray();

            using (HttpRequestMessage req11 = CreateSoap11Request("/manual/PaymentService.svc", "urn:echo/IEchoService/Echo", echoWithHeader))
            using (HttpResponseMessage res11 = await harness.Client.SendAsync(req11, TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, res11.StatusCode);
                Assert.Equal("text/xml; charset=utf-8", res11.Content.Headers.ContentType?.ToString());
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><s:Body><EchoResponse xmlns=\"urn:echo\"><EchoResult>SENSITIVE_ECHO_SECRET</EchoResult></EchoResponse></s:Body></s:Envelope>",
                    await res11.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            }

            byte[] echo12 = "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body><Echo xmlns=\"urn:echo\"><Message>soap12-ok</Message></Echo></s:Body></s:Envelope>"u8.ToArray();
            using (HttpRequestMessage req12 = CreateSoap12Request("/manual/PaymentService.svc", "urn:echo/IEchoService/Echo", echo12))
            using (HttpResponseMessage res12 = await harness.Client.SendAsync(req12, TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, res12.StatusCode);
                Assert.Equal("application/soap+xml; charset=utf-8", res12.Content.Headers.ContentType?.ToString());
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><s:Body><EchoResponse xmlns=\"urn:echo\"><EchoResult>soap12-ok</EchoResult></EchoResponse></s:Body></s:Envelope>",
                    await res12.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            }

            Assert.Equal(2, harness.Counter.EchoCount);
            Assert.Single(harness.HeaderHandler.Captured);

            List<Activity> matchingActivities;
            List<(string InstrumentName, Dictionary<string, object?> Tags)> matchingMetrics;
            lock (syncLock)
            {
                matchingActivities = recordedActivities
                    .Where(static a => string.Equals(a.GetTagItem("endpoint") as string, "/manual/PaymentService.svc", StringComparison.Ordinal))
                    .ToList();
                matchingMetrics = recordedMetrics
                    .Where(static m => m.Tags.TryGetValue("endpoint", out object? ep) && string.Equals(ep as string, "/manual/PaymentService.svc", StringComparison.Ordinal))
                    .ToList();
            }

            Assert.Equal(2, matchingActivities.Count);
            foreach (Activity activity in matchingActivities)
            {
                Assert.Equal(ActivityKind.Internal, activity.Kind);
                Assert.Equal("Echo", activity.GetTagItem("operation"));
                Assert.Equal("success", activity.GetTagItem("outcome"));
                Assert.Equal("none", activity.GetTagItem("fault.kind"));

                foreach (KeyValuePair<string, object?> tag in activity.TagObjects)
                {
                    string val = tag.Value?.ToString() ?? string.Empty;
                    Assert.DoesNotContain("SENSITIVE", val, StringComparison.Ordinal);
                    Assert.DoesNotContain("urn:echo/IEchoService/Echo", val, StringComparison.Ordinal);
                }
            }

            Assert.Contains(matchingMetrics, static m =>
                string.Equals(m.InstrumentName, "fastsoap.server.requests", StringComparison.Ordinal) &&
                string.Equals(m.Tags["operation"] as string, "Echo", StringComparison.Ordinal) &&
                string.Equals(m.Tags["soap.version"] as string, "soap11", StringComparison.Ordinal) &&
                string.Equals(m.Tags["outcome"] as string, "success", StringComparison.Ordinal));

            Assert.Contains(matchingMetrics, static m =>
                string.Equals(m.InstrumentName, "fastsoap.server.requests", StringComparison.Ordinal) &&
                string.Equals(m.Tags["operation"] as string, "Echo", StringComparison.Ordinal) &&
                string.Equals(m.Tags["soap.version"] as string, "soap12", StringComparison.Ordinal) &&
                string.Equals(m.Tags["outcome"] as string, "success", StringComparison.Ordinal));
        }

        [Fact]
        public async Task FallbackFault_WhenMaxResponseBytesTooSmall_FallsBackToHeaderOnly500()
        {
            await using KestrelHostHarness harness = await KestrelHostHarness.StartAsync();

            byte[] echoReq = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><Echo xmlns=\"urn:echo\"><Message>ok</Message></Echo></s:Body></s:Envelope>"u8.ToArray();
            using HttpRequestMessage req = CreateSoap11Request("/tiny-response-quota/PaymentService.svc", "urn:echo/IEchoService/Echo", echoReq);
            using HttpResponseMessage res = await harness.Client.SendAsync(req, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);
            Assert.Equal(0, res.Content.Headers.ContentLength);
            Assert.Empty(await res.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        }

        #endregion

        #region Private Methods

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

        private static HttpRequestMessage CreateSoap11Request(string path, string action, byte[] bodyUtf8)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, path);
            request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{action}\"");
            request.Content = new ByteArrayContent(bodyUtf8);
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("text/xml; charset=utf-8");

            return request;
        }

        private static HttpRequestMessage CreateSoap12Request(string path, string action, byte[] bodyUtf8)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, path);
            request.Content = new ByteArrayContent(bodyUtf8);
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse($"application/soap+xml; charset=utf-8; action=\"{action}\"");

            return request;
        }

        #endregion

        #region Nested Types

        private sealed class ChunkedBytesContent : HttpContent
        {

            #region Fields

            private readonly byte[] _payload;

            #endregion

            #region Constructors

            public ChunkedBytesContent(byte[] payload)
            {
                _payload = payload;
                Headers.ContentType = MediaTypeHeaderValue.Parse("text/xml; charset=utf-8");
            }

            #endregion

            #region Protected Methods

            protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            {
                int offset = 0;
                while (offset < _payload.Length)
                {
                    int chunk = Math.Min(512, _payload.Length - offset);
                    await stream.WriteAsync(_payload.AsMemory(offset, chunk)).ConfigureAwait(false);
                    offset += chunk;
                }
            }

            protected override bool TryComputeLength(out long length)
            {
                length = 0;

                return false;
            }

            #endregion

        }

        private sealed class KestrelHostHarness : IAsyncDisposable
        {

            #region Properties

            public HttpClient Client { get; }

            public InvocationCounter Counter { get; }

            public RecordingSoapHeaderHandler HeaderHandler { get; }

            public TaskCompletionSource<bool> SlowHandlerCanceled { get; }

            public TaskCompletionSource<bool> SlowHandlerEntered { get; }

            private WebApplication App { get; }

            #endregion

            #region Constructors

            private KestrelHostHarness(
                WebApplication app,
                HttpClient client,
                InvocationCounter counter,
                RecordingSoapHeaderHandler headerHandler,
                TaskCompletionSource<bool> slowHandlerEntered,
                TaskCompletionSource<bool> slowHandlerCanceled)
            {
                App = app;
                Client = client;
                Counter = counter;
                HeaderHandler = headerHandler;
                SlowHandlerEntered = slowHandlerEntered;
                SlowHandlerCanceled = slowHandlerCanceled;
            }

            #endregion

            #region Public Methods

            public async ValueTask DisposeAsync()
            {
                Client.Dispose();
                await App.StopAsync().ConfigureAwait(false);
                await App.DisposeAsync().ConfigureAwait(false);
            }

            public static async Task<KestrelHostHarness> StartAsync()
            {
                WebApplicationBuilder builder = WebApplication.CreateBuilder();
                builder.WebHost.UseUrls("http://127.0.0.1:0");
                builder.Logging.ClearProviders();

                InvocationCounter counter = new InvocationCounter();
                RecordingSoapHeaderHandler headerHandler = new RecordingSoapHeaderHandler(new SoapQName("urn:headers", "TraceId"));
                TaskCompletionSource<bool> slowEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                TaskCompletionSource<bool> slowCanceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                builder.Services.AddSingleton(counter);
                builder.Services.AddScoped<IIntegrationPaymentService, IntegrationPaymentService>();

                builder.Services.AddAuthentication(TestBearerAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestBearerAuthenticationHandler>(
                        TestBearerAuthenticationHandler.SchemeName,
                        static _ => { });

                builder.Services.AddAuthorizationBuilder()
                    .AddPolicy("RequirePaymentsWriter", static policy =>
                        policy.RequireRole("PaymentsWriter"));

                builder.Services.AddFastSoap(static options =>
                {
                    options.EnabledVersions = SoapVersionFlags.Both;
                });

                WebApplication app = builder.Build();
                app.UseAuthentication();
                app.UseAuthorization();

                XmlSerializerOptions strictOptions = new XmlSerializerOptions
                {
                    AllowDuplicateProperties = false,
                    OmitXmlDeclaration = true,
                    UnmappedMemberHandling = XmlUnmappedMemberHandling.Disallow
                };
                IntegrationXmlContext manualContext = new IntegrationXmlContext(strictOptions);

                SoapServiceMetadata metadata = IntegrationMetadataFactory.CreatePaymentMetadata();
                SoapMetadataAddressPolicy addressPolicy = new SoapMetadataAddressPolicy("https://payments.example.com/manual/PaymentService.svc");

                app.MapIntegrationPaymentService("/generated/PaymentService.svc");

                app.MapIntegrationPaymentService("/configured/PaymentService.svc", soap =>
                {
                    soap.ConfigureOptions(static options =>
                    {
                        options.EnabledVersions = SoapVersionFlags.Soap12;
                        options.EnableMetadata = true;
                    });
                    soap.MapGeneratedMetadata(
                        metadata,
                        new SoapMetadataAddressPolicy("https://payments.example.com/configured/PaymentService.svc"),
                        SoapWsdlGenerationMode.InlineSchemas);
                });

                app.MapSoap("/manual/PaymentService.svc", soap =>
                {
                    soap.ConfigureOptions(static options =>
                    {
                        options.EnabledVersions = SoapVersionFlags.Both;
                        options.EnableMetadata = true;
                    });
                    soap.AddHeaderHandler(headerHandler);
                    soap.MapGeneratedMetadata(metadata, addressPolicy, SoapWsdlGenerationMode.ExternalSchemas);

                    soap.MapOperation(
                        descriptor: EchoDescriptor,
                        requestType: manualContext.EchoRequest,
                        responseType: manualContext.EchoResponse,
                        handler: (request, context, ct) =>
                        {
                            ct.ThrowIfCancellationRequested();
                            counter.IncrementEcho();

                            return ValueTask.FromResult(new EchoResponse { EchoResult = request.Message });
                        });

                    soap.MapOperation(
                        descriptor: IntegrationPaymentServiceSoapContract.Transfer,
                        requestType: manualContext.TransferRequestWire,
                        responseType: manualContext.TransferResponseWire,
                        handler: static async (wire, context, ct) =>
                        {
                            IIntegrationPaymentService service = context.Services.GetRequiredService<IIntegrationPaymentService>();
                            TransferResult result = await service.TransferAsync(wire.Request, ct).ConfigureAwait(false);

                            return new TransferResponseWire { Result = result };
                        });
                });

                app.MapIntegrationPaymentService("/secure/PaymentService.svc")
                    .RequireAuthorization("RequirePaymentsWriter");

                app.MapSoap("/quota/PaymentService.svc", soap =>
                {
                    soap.ConfigureOptions(static options =>
                    {
                        options.EnabledVersions = SoapVersionFlags.Soap11;
                        options.MaxRequestBytes = 1024;
                    });

                    soap.MapOperation(
                        descriptor: EchoDescriptor,
                        requestType: manualContext.EchoRequest,
                        responseType: manualContext.EchoResponse,
                        handler: (request, _, _) =>
                        {
                            counter.IncrementEcho();

                            return ValueTask.FromResult(new EchoResponse { EchoResult = request.Message });
                        });
                });

                app.MapSoap("/slow/PaymentService.svc", soap =>
                {
                    soap.MapOperation(
                        descriptor: EchoDescriptor,
                        requestType: manualContext.EchoRequest,
                        responseType: manualContext.EchoResponse,
                        handler: async (request, _, ct) =>
                        {
                            slowEntered.TrySetResult(true);
                            try
                            {
                                await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                            {
                                slowCanceled.TrySetResult(true);
                                throw;
                            }

                            return new EchoResponse { EchoResult = request.Message };
                        });
                });

                app.MapSoap("/tiny-response-quota/PaymentService.svc", soap =>
                {
                    soap.ConfigureOptions(static options =>
                    {
                        options.EnabledVersions = SoapVersionFlags.Soap11;
                        options.MaxResponseBytes = 32;
                    });

                    soap.MapOperation(
                        descriptor: EchoDescriptor,
                        requestType: manualContext.EchoRequest,
                        responseType: manualContext.EchoResponse,
                        handler: static (request, _, _) => ValueTask.FromResult(new EchoResponse { EchoResult = request.Message }));
                });

                await app.StartAsync().ConfigureAwait(false);

                IServer server = app.Services.GetRequiredService<IServer>();
                IServerAddressesFeature addresses = server.Features.Get<IServerAddressesFeature>()!;
                string baseAddress = addresses.Addresses.First();

                HttpClient client = new HttpClient
                {
                    BaseAddress = new Uri(baseAddress)
                };

                return new KestrelHostHarness(
                    app,
                    client,
                    counter,
                    headerHandler,
                    slowEntered,
                    slowCanceled);
            }

            #endregion

        }

        #endregion

    }

}
