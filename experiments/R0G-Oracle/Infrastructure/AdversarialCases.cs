using System;
using System.Collections.Generic;
using System.Text;

using FastSoap.R0G.Oracle.Contracts;

namespace FastSoap.R0G.Oracle.Infrastructure
{
    /// <summary>
    /// Describes an adversarial or negative protocol/XML test vector sent to CoreWCF and saved as an immutable fixture.
    /// </summary>
    public sealed class AdversarialTestCase
    {
        #region Properties

        /// <summary>
        /// Gets the expected CoreWCF HTTP status code observed on the oracle run.
        /// </summary>
        public required int ExpectedCoreWcfStatusCode { get; init; }

        /// <summary>
        /// Gets the expected FastSoap rejection policy summary (from FastSoap-Implementation-Plan-v2.md).
        /// </summary>
        public required string ExpectedFastSoapOutcome { get; init; }

        /// <summary>
        /// Gets the fixture base file name (without extension).
        /// </summary>
        public required string FileBaseName { get; init; }

        /// <summary>
        /// Gets the canonical fixture identifier recorded in <c>compatibility-corpus.md</c>.
        /// </summary>
        public required string FixtureId { get; init; }

        /// <summary>
        /// Gets a value indicating whether the payload contains intentionally malformed binary bytes rather than valid UTF-8 XML.
        /// </summary>
        public bool IsBinaryRequest { get; init; }

        /// <summary>
        /// Gets the mapped CoreWCF test suite or upstream specification reference.
        /// </summary>
        public required string MappedCoreWcfTest { get; init; }

        /// <summary>
        /// Gets the target endpoint path on <see cref="WireCaptureHost"/>.
        /// </summary>
        public required string Path { get; init; }

        /// <summary>
        /// Gets the raw request bytes to transmit verbatim.
        /// </summary>
        public required byte[] RequestBody { get; init; }

        /// <summary>
        /// Gets the HTTP <c>Content-Type</c> header value.
        /// </summary>
        public required string RequestContentType { get; init; }

        /// <summary>
        /// Gets the optional HTTP <c>SOAPAction</c> header value.
        /// </summary>
        public string? RequestSoapAction { get; init; }

        /// <summary>
        /// Gets the SOAP version targeted by the vector.
        /// </summary>
        public required string SoapVersion { get; init; }

        #endregion
    }

    /// <summary>
    /// Builds the deterministic negative/adversarial XML and SOAP protocol test vectors.
    /// </summary>
    public static class AdversarialCases
    {
        #region Public Methods

        /// <summary>
        /// Returns all negative and adversarial test vectors required by R0-G / G1.
        /// </summary>
        /// <returns>An ordered read-only list of adversarial test cases.</returns>
        public static IReadOnlyList<AdversarialTestCase> GetAll()
        {
            List<AdversarialTestCase> cases =
            [
                new AdversarialTestCase
                {
                    FixtureId = "NEG-01",
                    FileBaseName = "neg01-dtd-xxe",
                    SoapVersion = "Soap11",
                    Path = "/soap11/echo-dcs.svc",
                    RequestContentType = "text/xml; charset=utf-8",
                    RequestSoapAction = $"\"{EchoConstants.EchoStringAction}\"",
                    RequestBody = Encoding.UTF8.GetBytes(
                        "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                        "<!DOCTYPE foo [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]>" +
                        "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                        "<s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>&xxe;</echo></EchoString></s:Body>" +
                        "</s:Envelope>"),
                    ExpectedCoreWcfStatusCode = 400,
                    ExpectedFastSoapOutcome = "Reject before service invocation (DTD prohibited, HTTP 400 / Sender fault)",
                    MappedCoreWcfTest = "CoreWCF TextMessageEncoder / XmlDictionaryReaderQuotas (DtdProcessing.Prohibit) + FastSoap Plan §11"
                },
                new AdversarialTestCase
                {
                    FixtureId = "NEG-02",
                    FileBaseName = "neg02-dtd-entity-expansion",
                    SoapVersion = "Soap11",
                    Path = "/soap11/echo-dcs.svc",
                    RequestContentType = "text/xml; charset=utf-8",
                    RequestSoapAction = $"\"{EchoConstants.EchoStringAction}\"",
                    RequestBody = Encoding.UTF8.GetBytes(
                        "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                        "<!DOCTYPE lolz [<!ENTITY lol \"lol\"><!ENTITY lol2 \"&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;\">]>" +
                        "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                        "<s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>&lol2;</echo></EchoString></s:Body>" +
                        "</s:Envelope>"),
                    ExpectedCoreWcfStatusCode = 400,
                    ExpectedFastSoapOutcome = "Reject before service invocation (DTD/entity expansion prohibited, HTTP 400)",
                    MappedCoreWcfTest = "CoreWCF TextMessageEncoder / XmlDictionaryReaderQuotas (DtdProcessing.Prohibit) + FastSoap Plan §11"
                },
                new AdversarialTestCase
                {
                    FixtureId = "NEG-03A",
                    FileBaseName = "neg03a-malformed-utf8",
                    SoapVersion = "Soap11",
                    IsBinaryRequest = true,
                    Path = "/soap11/echo-dcs.svc",
                    RequestContentType = "text/xml; charset=utf-8",
                    RequestSoapAction = $"\"{EchoConstants.EchoStringAction}\"",
                    RequestBody = BuildMalformedUtf8Payload(),
                    ExpectedCoreWcfStatusCode = 500,
                    ExpectedFastSoapOutcome = "Reject invalid UTF-8 byte sequences before service invocation (HTTP 400)",
                    MappedCoreWcfTest = "CoreWCF TextMessageEncoder UTF-8 decoding + FastSoap Plan §6.1 & §11"
                },
                new AdversarialTestCase
                {
                    FixtureId = "NEG-03B",
                    FileBaseName = "neg03b-invalid-xml10-char",
                    SoapVersion = "Soap11",
                    IsBinaryRequest = true,
                    Path = "/soap11/echo-dcs.svc",
                    RequestContentType = "text/xml; charset=utf-8",
                    RequestSoapAction = $"\"{EchoConstants.EchoStringAction}\"",
                    RequestBody = BuildInvalidXml10ControlCharPayload(),
                    ExpectedCoreWcfStatusCode = 500,
                    ExpectedFastSoapOutcome = "Reject invalid XML 1.0 control character (0x0F) before service invocation (HTTP 400)",
                    MappedCoreWcfTest = "CoreWCF.Primitives.Tests.MessageTests.InvalidCharToString"
                },
                new AdversarialTestCase
                {
                    FixtureId = "NEG-04",
                    FileBaseName = "neg04-duplicate-body-soap11",
                    SoapVersion = "Soap11",
                    Path = "/soap11/echo-dcs.svc",
                    RequestContentType = "text/xml; charset=utf-8",
                    RequestSoapAction = $"\"{EchoConstants.EchoStringAction}\"",
                    RequestBody = Encoding.UTF8.GetBytes(
                        "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                        "<s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>one</echo></EchoString></s:Body>" +
                        "<s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>two</echo></EchoString></s:Body>" +
                        "</s:Envelope>"),
                    ExpectedCoreWcfStatusCode = 500,
                    ExpectedFastSoapOutcome = "Reject duplicate Body element per SOAP 1.1 envelope grammar (HTTP 400 / Client fault)",
                    MappedCoreWcfTest = "CoreWCF Envelope/Message reader validation + FastSoap Plan §6.2"
                },
                new AdversarialTestCase
                {
                    FixtureId = "NEG-05",
                    FileBaseName = "neg05-wrong-ordering-soap11",
                    SoapVersion = "Soap11",
                    Path = "/soap11/echo-dcs.svc",
                    RequestContentType = "text/xml; charset=utf-8",
                    RequestSoapAction = $"\"{EchoConstants.EchoStringAction}\"",
                    RequestBody = Encoding.UTF8.GetBytes(
                        "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                        "<s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>hello</echo></EchoString></s:Body>" +
                        "<s:Header><x:Trace xmlns:x=\"urn:test\">1</x:Trace></s:Header>" +
                        "</s:Envelope>"),
                    ExpectedCoreWcfStatusCode = 500,
                    ExpectedFastSoapOutcome = "Reject Header after Body per SOAP 1.1 envelope grammar (HTTP 400 / Client fault)",
                    MappedCoreWcfTest = "CoreWCF Envelope/Message reader validation + FastSoap Plan §6.2"
                },
                new AdversarialTestCase
                {
                    FixtureId = "NEG-06A",
                    FileBaseName = "neg06a-unknown-mustunderstand-soap11",
                    SoapVersion = "Soap11",
                    Path = "/soap11/echo-dcs.svc",
                    RequestContentType = "text/xml; charset=utf-8",
                    RequestSoapAction = $"\"{EchoConstants.EchoStringAction}\"",
                    RequestBody = Encoding.UTF8.GetBytes(
                        "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                        "<s:Header><x:UnknownMandatory xmlns:x=\"urn:unknown\" s:mustUnderstand=\"1\">required</x:UnknownMandatory></s:Header>" +
                        "<s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>hello</echo></EchoString></s:Body>" +
                        "</s:Envelope>"),
                    ExpectedCoreWcfStatusCode = 500,
                    ExpectedFastSoapOutcome = "SOAP 1.1 MustUnderstand fault (HTTP 500, faultcode s:MustUnderstand); zero service invocations",
                    MappedCoreWcfTest = "CoreWCF MessageHeaders.UnderstoodHeaders / MustUnderstand processing + FastSoap Plan §6.3"
                },
                new AdversarialTestCase
                {
                    FixtureId = "NEG-06B",
                    FileBaseName = "neg06b-unknown-mustunderstand-soap12",
                    SoapVersion = "Soap12",
                    Path = "/soap12/echo-dcs.svc",
                    RequestContentType = $"application/soap+xml; charset=utf-8; action=\"{EchoConstants.EchoStringAction}\"",
                    RequestSoapAction = null,
                    RequestBody = Encoding.UTF8.GetBytes(
                        "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\">" +
                        "<s:Header><x:UnknownMandatory xmlns:x=\"urn:unknown\" s:mustUnderstand=\"true\">required</x:UnknownMandatory></s:Header>" +
                        "<s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>hello</echo></EchoString></s:Body>" +
                        "</s:Envelope>"),
                    ExpectedCoreWcfStatusCode = 500,
                    ExpectedFastSoapOutcome = "SOAP 1.2 MustUnderstand fault with NotUnderstood header (faultcode s:MustUnderstand); zero service invocations",
                    MappedCoreWcfTest = "CoreWCF SOAP 1.2 MustUnderstand + NotUnderstood header semantics + FastSoap Plan §6.3"
                },
                new AdversarialTestCase
                {
                    FixtureId = "NEG-07",
                    FileBaseName = "neg07-action-body-mismatch-soap11",
                    SoapVersion = "Soap11",
                    Path = "/soap11/payment-dcs-urn.svc",
                    RequestContentType = "text/xml; charset=utf-8",
                    RequestSoapAction = $"\"{PaymentConstants.TransferAction}\"",
                    RequestBody = Encoding.UTF8.GetBytes(
                        "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                        "<s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>mismatch</echo></EchoString></s:Body>" +
                        "</s:Envelope>"),
                    ExpectedCoreWcfStatusCode = 500,
                    ExpectedFastSoapOutcome = "Reject action/body QName mismatch with SOAP fault; zero service invocations",
                    MappedCoreWcfTest = "CoreWCF.Http.Tests.OpActionReplyActionBehaviorTests + FastSoap Plan §4 & §6.1"
                },
                new AdversarialTestCase
                {
                    FixtureId = "NEG-08",
                    FileBaseName = "neg08-oversized-payload-soap11",
                    SoapVersion = "Soap11",
                    Path = "/soap11/echo-dcs.svc",
                    RequestContentType = "text/xml; charset=utf-8",
                    RequestSoapAction = $"\"{EchoConstants.EchoStringAction}\"",
                    RequestBody = BuildOversizedPayload(70 * 1024),
                    ExpectedCoreWcfStatusCode = 413,
                    ExpectedFastSoapOutcome = "Reject payload exceeding MaxRequestBytes (HTTP 413 PayloadTooLarge per FastSoap Plan §6.4)",
                    MappedCoreWcfTest = "CoreWCF.Http.Tests.RequestDelegateHandlerMaxBodySizeTests & LargeRequestTests (65536 default quota)"
                },
                new AdversarialTestCase
                {
                    FixtureId = "NEG-09",
                    FileBaseName = "neg09-utf16-encoding-mismatch",
                    SoapVersion = "Soap11",
                    Path = "/soap11/echo-dcs.svc",
                    RequestContentType = "text/xml; charset=utf-16",
                    RequestSoapAction = $"\"{EchoConstants.EchoStringAction}\"",
                    RequestBody = Encoding.UTF8.GetBytes(
                        "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                        "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                        "<s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>utf16</echo></EchoString></s:Body>" +
                        "</s:Envelope>"),
                    ExpectedCoreWcfStatusCode = 400,
                    ExpectedFastSoapOutcome = "Reject non-UTF-8 charset / declaration mismatch (HTTP 415 UnsupportedMediaType)",
                    MappedCoreWcfTest = "CoreWCF TextMessageEncoder UTF-8 encoding enforcement + FastSoap Plan §6.1"
                },
                new AdversarialTestCase
                {
                    FixtureId = "NEG-10",
                    FileBaseName = "neg10-non-envelope-root",
                    SoapVersion = "Soap11",
                    Path = "/soap11/payment-dcs-urn.svc",
                    RequestContentType = "text/xml; charset=utf-8",
                    RequestSoapAction = $"\"{PaymentConstants.TransferAction}\"",
                    RequestBody = Encoding.UTF8.GetBytes(
                        "<Transfer xmlns=\"urn:payments\"><request><AccountNumber>123456</AccountNumber></request></Transfer>"),
                    ExpectedCoreWcfStatusCode = 400,
                    ExpectedFastSoapOutcome = "Reject non-Envelope root element (HTTP 400 / Client fault)",
                    MappedCoreWcfTest = "CoreWCF Message.CreateMessage envelope check + FastSoap Plan §6.2"
                },
                new AdversarialTestCase
                {
                    FixtureId = "NEG-11",
                    FileBaseName = "neg11-trailing-root",
                    SoapVersion = "Soap11",
                    Path = "/soap11/echo-dcs.svc",
                    RequestContentType = "text/xml; charset=utf-8",
                    RequestSoapAction = $"\"{EchoConstants.EchoStringAction}\"",
                    RequestBody = Encoding.UTF8.GetBytes(
                        "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                        "<s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>hello</echo></EchoString></s:Body>" +
                        "</s:Envelope><TrailingRoot />"),
                    ExpectedCoreWcfStatusCode = 500,
                    ExpectedFastSoapOutcome = "Reject trailing XML root content (HTTP 400); zero service invocations",
                    MappedCoreWcfTest = "CoreWCF XmlReader trailing content rejection + FastSoap Plan §5.2 & §6.2"
                },
                new AdversarialTestCase
                {
                    FixtureId = "NEG-12",
                    FileBaseName = "neg12-missing-soapaction-soap11",
                    SoapVersion = "Soap11",
                    Path = "/soap11/echo-dcs.svc",
                    RequestContentType = "text/xml; charset=utf-8",
                    RequestSoapAction = null,
                    RequestBody = Encoding.UTF8.GetBytes(
                        "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                        "<s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>hello</echo></EchoString></s:Body>" +
                        "</s:Envelope>"),
                    ExpectedCoreWcfStatusCode = 500,
                    ExpectedFastSoapOutcome = "Reject missing SOAPAction in strict SOAP 1.1 mode (HTTP 400 / Client fault)",
                    MappedCoreWcfTest = "CoreWCF BasicHttpBinding missing SOAPAction handling + FastSoap Plan §6.1"
                },
                new AdversarialTestCase
                {
                    FixtureId = "NEG-13",
                    FileBaseName = "neg13-version-mismatch-soap12-on-soap11",
                    SoapVersion = "Soap11",
                    Path = "/soap11/echo-dcs.svc",
                    RequestContentType = "text/xml; charset=utf-8",
                    RequestSoapAction = $"\"{EchoConstants.EchoStringAction}\"",
                    RequestBody = Encoding.UTF8.GetBytes(
                        "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\">" +
                        "<s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>hello</echo></EchoString></s:Body>" +
                        "</s:Envelope>"),
                    ExpectedCoreWcfStatusCode = 400,
                    ExpectedFastSoapOutcome = "SOAP VersionMismatch fault (HTTP 500, faultcode s:VersionMismatch); zero service invocations",
                    MappedCoreWcfTest = "CoreWCF.Http.Tests.MessageVersionsTests + FastSoap Plan §6.1 & §6.4"
                },
                new AdversarialTestCase
                {
                    FixtureId = "NEG-14A",
                    FileBaseName = "neg14a-pi-in-envelope",
                    SoapVersion = "Soap11",
                    Path = "/soap11/echo-dcs.svc",
                    RequestContentType = "text/xml; charset=utf-8",
                    RequestSoapAction = $"\"{EchoConstants.EchoStringAction}\"",
                    RequestBody = Encoding.UTF8.GetBytes(
                        "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                        "<?pi envelope-instruction?>" +
                        "<s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>hello</echo></EchoString></s:Body>" +
                        "</s:Envelope>"),
                    ExpectedCoreWcfStatusCode = 400,
                    ExpectedFastSoapOutcome = "Reject processing instruction inside SOAP envelope (HTTP 400 / Client fault); zero service invocations",
                    MappedCoreWcfTest = "SOAP 1.1 §3 / SOAP 1.2 Part 1 §5 (prohibited processing instructions) + FastSoap Plan §6.2 & ADR-001 §3.3"
                },
                new AdversarialTestCase
                {
                    FixtureId = "NEG-14B",
                    FileBaseName = "neg14b-pi-in-body",
                    SoapVersion = "Soap11",
                    Path = "/soap11/echo-dcs.svc",
                    RequestContentType = "text/xml; charset=utf-8",
                    RequestSoapAction = $"\"{EchoConstants.EchoStringAction}\"",
                    RequestBody = Encoding.UTF8.GetBytes(
                        "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                        "<s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>hello<?pi body-instruction?></echo></EchoString></s:Body>" +
                        "</s:Envelope>"),
                    ExpectedCoreWcfStatusCode = 500,
                    ExpectedFastSoapOutcome = "Reject processing instruction inside deserialized body element (HTTP 400 / Client fault); zero service invocations",
                    MappedCoreWcfTest = "SOAP 1.1 §3 / SOAP 1.2 Part 1 §5 + ADR-001 §3.2 #1(h) & §3.3 BLOCKER-2"
                },
                new AdversarialTestCase
                {
                    FixtureId = "NEG-15",
                    FileBaseName = "neg15-xml11-declaration",
                    SoapVersion = "Soap11",
                    Path = "/soap11/echo-dcs.svc",
                    RequestContentType = "text/xml; charset=utf-8",
                    RequestSoapAction = $"\"{EchoConstants.EchoStringAction}\"",
                    RequestBody = Encoding.UTF8.GetBytes(
                        "<?xml version='1.1' encoding='utf-8'?>" +
                        "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                        "<s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>hello</echo></EchoString></s:Body>" +
                        "</s:Envelope>"),
                    ExpectedCoreWcfStatusCode = 400,
                    ExpectedFastSoapOutcome = "Reject XML 1.1 declaration (XML 1.0 only per FastSoap Plan §6.2, HTTP 400); zero service invocations",
                    MappedCoreWcfTest = "CoreWCF TextMessageEncoder (XML 1.0 only) + FastSoap Plan §6.2 & ADR-001 §3.3 BLOCKER-2"
                },
                new AdversarialTestCase
                {
                    FixtureId = "NEG-16A",
                    FileBaseName = "neg16a-truncated-utf8-bom",
                    SoapVersion = "Soap11",
                    IsBinaryRequest = true,
                    Path = "/soap11/echo-dcs.svc",
                    RequestContentType = "text/xml; charset=utf-8",
                    RequestSoapAction = $"\"{EchoConstants.EchoStringAction}\"",
                    RequestBody = BuildTruncatedUtf8BomPayload(),
                    ExpectedCoreWcfStatusCode = 400,
                    ExpectedFastSoapOutcome = "Reject truncated/invalid UTF-8 BOM prefix before service invocation (HTTP 400)",
                    MappedCoreWcfTest = "CoreWCF TextMessageEncoder UTF-8 BOM validation + FastSoap Plan §6.1 & ADR-001 §3.3 BLOCKER-2"
                },
                new AdversarialTestCase
                {
                    FixtureId = "NEG-16B",
                    FileBaseName = "neg16b-utf16-bom-utf8-charset",
                    SoapVersion = "Soap11",
                    IsBinaryRequest = true,
                    Path = "/soap11/echo-dcs.svc",
                    RequestContentType = "text/xml; charset=utf-8",
                    RequestSoapAction = $"\"{EchoConstants.EchoStringAction}\"",
                    RequestBody = BuildUtf16LeBomWithUtf8BodyPayload(),
                    ExpectedCoreWcfStatusCode = 400,
                    ExpectedFastSoapOutcome = "Reject non-UTF-8 BOM (UTF-16 LE 0xFF 0xFE) with text/xml; charset=utf-8 (HTTP 400 / 415); zero service invocations",
                    MappedCoreWcfTest = "CoreWCF TextMessageEncoder BOM/charset validation + FastSoap Plan §6.1 & ADR-001 §3.3 BLOCKER-2"
                },
                new AdversarialTestCase
                {
                    FixtureId = "NEG-17A",
                    FileBaseName = "neg17a-encoding-style-envelope-soap11",
                    SoapVersion = "Soap11",
                    Path = "/soap11/echo-dcs.svc",
                    RequestContentType = "text/xml; charset=utf-8",
                    RequestSoapAction = $"\"{EchoConstants.EchoStringAction}\"",
                    RequestBody = Encoding.UTF8.GetBytes(
                        "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                        "<s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>hello</echo></EchoString></s:Body>" +
                        "</s:Envelope>"),
                    ExpectedCoreWcfStatusCode = 200,
                    ExpectedFastSoapOutcome = "Reject soap:encodingStyle attribute on Envelope (document/literal only per FastSoap Plan §6.2 & ADR-001 §3.2, HTTP 400)",
                    MappedCoreWcfTest = "FastSoap Plan §1.1 & §6.2 + ADR-001 §3.2 #2 & §3.3 BLOCKER-2"
                },
                new AdversarialTestCase
                {
                    FixtureId = "NEG-17B",
                    FileBaseName = "neg17b-encoding-style-body-soap11",
                    SoapVersion = "Soap11",
                    Path = "/soap11/echo-dcs.svc",
                    RequestContentType = "text/xml; charset=utf-8",
                    RequestSoapAction = $"\"{EchoConstants.EchoStringAction}\"",
                    RequestBody = Encoding.UTF8.GetBytes(
                        "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                        "<s:Body s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><EchoString xmlns=\"http://tempuri.org/\"><echo>hello</echo></EchoString></s:Body>" +
                        "</s:Envelope>"),
                    ExpectedCoreWcfStatusCode = 200,
                    ExpectedFastSoapOutcome = "Reject soap:encodingStyle attribute on Body (document/literal only per FastSoap Plan §6.2 & ADR-001 §3.2, HTTP 400)",
                    MappedCoreWcfTest = "FastSoap Plan §1.1 & §6.2 + ADR-001 §3.2 #2 & §3.3 BLOCKER-2"
                },
                new AdversarialTestCase
                {
                    FixtureId = "NEG-18A",
                    FileBaseName = "neg18a-excessive-depth-soap11",
                    SoapVersion = "Soap11",
                    Path = "/soap11/echo-dcs.svc",
                    RequestContentType = "text/xml; charset=utf-8",
                    RequestSoapAction = $"\"{EchoConstants.EchoStringAction}\"",
                    RequestBody = BuildExcessiveDepthPayload(70),
                    ExpectedCoreWcfStatusCode = 500,
                    ExpectedFastSoapOutcome = "Reject XML nesting exceeding MaxXmlDepth (64) before service invocation (HTTP 400)",
                    MappedCoreWcfTest = "CoreWCF XmlDictionaryReaderQuotas.MaxDepth + FastSoap Plan §8 & ADR-001 §3.3 BLOCKER-2"
                },
                new AdversarialTestCase
                {
                    FixtureId = "NEG-18B",
                    FileBaseName = "neg18b-excessive-attributes-soap11",
                    SoapVersion = "Soap11",
                    Path = "/soap11/echo-dcs.svc",
                    RequestContentType = "text/xml; charset=utf-8",
                    RequestSoapAction = $"\"{EchoConstants.EchoStringAction}\"",
                    RequestBody = BuildExcessiveAttributesPayload(70),
                    ExpectedCoreWcfStatusCode = 200,
                    ExpectedFastSoapOutcome = "Reject element exceeding MaxAttributesPerElement (64) before service invocation (HTTP 400)",
                    MappedCoreWcfTest = "FastSoap Plan §8 & ADR-001 §3.2 #1 (MaxAttributeCount=64) & §3.3 BLOCKER-2"
                },
                new AdversarialTestCase
                {
                    FixtureId = "NEG-18C",
                    FileBaseName = "neg18c-excessive-header-count-soap11",
                    SoapVersion = "Soap11",
                    Path = "/soap11/echo-dcs.svc",
                    RequestContentType = "text/xml; charset=utf-8",
                    RequestSoapAction = $"\"{EchoConstants.EchoStringAction}\"",
                    RequestBody = BuildExcessiveHeaderCountPayload(40),
                    ExpectedCoreWcfStatusCode = 200,
                    ExpectedFastSoapOutcome = "Reject SOAP Envelope exceeding MaxHeaderCount (32) before service invocation (HTTP 400)",
                    MappedCoreWcfTest = "FastSoap Plan §6.3 & §8 (MaxHeaderCount=32) + ADR-001 §3.2 #2 & §3.3 BLOCKER-2"
                },
                new AdversarialTestCase
                {
                    FixtureId = "NEG-18D",
                    FileBaseName = "neg18d-oversized-header-block-soap11",
                    SoapVersion = "Soap11",
                    Path = "/soap11/echo-dcs.svc",
                    RequestContentType = "text/xml; charset=utf-8",
                    RequestSoapAction = $"\"{EchoConstants.EchoStringAction}\"",
                    RequestBody = BuildOversizedHeaderBlockPayload(68 * 1024),
                    ExpectedCoreWcfStatusCode = 413,
                    ExpectedFastSoapOutcome = "Reject SOAP Header block exceeding MaxHeaderBytes (64 KiB) before service invocation (HTTP 400 / 413)",
                    MappedCoreWcfTest = "FastSoap Plan §6.3 & §8 (MaxHeaderBytes=64KiB) + ADR-001 §3.2 #2 & §3.3 BLOCKER-2"
                },
                new AdversarialTestCase
                {
                    FixtureId = "NEG-19",
                    FileBaseName = "neg19-duplicate-dto-element-soap11",
                    SoapVersion = "Soap11",
                    Path = "/soap11/payment-xmlser.svc",
                    RequestContentType = "text/xml; charset=utf-8",
                    RequestSoapAction = $"\"{PaymentConstants.TransferAction}\"",
                    RequestBody = Encoding.UTF8.GetBytes(
                        "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                        "<s:Body>" +
                        "<Transfer xmlns=\"urn:payments\">" +
                        "<request>" +
                        "<AccountNumber>123456</AccountNumber>" +
                        "<AccountNumber>999999</AccountNumber>" +
                        "<Amount>100.50</Amount>" +
                        "<Currency>USD</Currency>" +
                        "</request>" +
                        "</Transfer>" +
                        "</s:Body>" +
                        "</s:Envelope>"),
                    ExpectedCoreWcfStatusCode = 200,
                    ExpectedFastSoapOutcome = "Reject duplicate XML element in DTO payload (AllowDuplicateProperties=false per FastSoap Plan §7.2 & ADR-001 §3.2, HTTP 400)",
                    MappedCoreWcfTest = "FastSoap Plan §7.2 & §11 (duplicate fields) + ADR-001 §3.2 #1 & §3.3 BLOCKER-2"
                }
            ];

            return cases;
        }

        #endregion

        #region Private Methods

        private static byte[] BuildExcessiveAttributesPayload(int attributeCount)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body>");
            sb.Append("<EchoString xmlns=\"http://tempuri.org/\"");
            for (int index = 0; index < attributeCount; index++)
            {
                sb.Append(" a");
                sb.Append(index);
                sb.Append("=\"");
                sb.Append(index);
                sb.Append('"');
            }

            sb.Append("><echo>hello</echo></EchoString></s:Body></s:Envelope>");

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        private static byte[] BuildExcessiveDepthPayload(int depth)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\">");
            sb.Append("<s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>");
            for (int index = 0; index < depth; index++)
            {
                sb.Append("<d>");
            }

            sb.Append("deep");
            for (int index = 0; index < depth; index++)
            {
                sb.Append("</d>");
            }

            sb.Append("</echo></EchoString></s:Body></s:Envelope>");

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        private static byte[] BuildExcessiveHeaderCountPayload(int headerCount)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Header>");
            for (int index = 0; index < headerCount; index++)
            {
                sb.Append("<x:Trace xmlns:x=\"urn:test\">");
                sb.Append(index);
                sb.Append("</x:Trace>");
            }

            sb.Append("</s:Header><s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>hello</echo></EchoString></s:Body></s:Envelope>");

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        private static byte[] BuildInvalidXml10ControlCharPayload()
        {
            byte[] prefix = Encoding.UTF8.GetBytes(
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                "<s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>string");
            byte[] suffix = Encoding.UTF8.GetBytes(
                "</echo></EchoString></s:Body></s:Envelope>");

            byte[] result = new byte[prefix.Length + 1 + suffix.Length];
            Buffer.BlockCopy(prefix, 0, result, 0, prefix.Length);
            result[prefix.Length] = 0x0F;
            Buffer.BlockCopy(suffix, 0, result, prefix.Length + 1, suffix.Length);

            return result;
        }

        private static byte[] BuildMalformedUtf8Payload()
        {
            byte[] prefix = Encoding.UTF8.GetBytes(
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                "<s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>");
            byte[] invalidUtf8 = [0xFF, 0xFE, 0xC3, 0x28];
            byte[] suffix = Encoding.UTF8.GetBytes(
                "</echo></EchoString></s:Body></s:Envelope>");

            byte[] result = new byte[prefix.Length + invalidUtf8.Length + suffix.Length];
            Buffer.BlockCopy(prefix, 0, result, 0, prefix.Length);
            Buffer.BlockCopy(invalidUtf8, 0, result, prefix.Length, invalidUtf8.Length);
            Buffer.BlockCopy(suffix, 0, result, prefix.Length + invalidUtf8.Length, suffix.Length);

            return result;
        }

        private static byte[] BuildOversizedHeaderBlockPayload(int headerContentBytes)
        {
            string prefix =
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                "<s:Header><x:LargeHeader xmlns:x=\"urn:test\">";
            string suffix =
                "</x:LargeHeader></s:Header>" +
                "<s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>hello</echo></EchoString></s:Body>" +
                "</s:Envelope>";
            string payload = prefix + new string('H', headerContentBytes) + suffix;

            return Encoding.UTF8.GetBytes(payload);
        }

        private static byte[] BuildOversizedPayload(int targetSizeBytes)
        {
            string header =
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                "<s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>";
            string footer = "</echo></EchoString></s:Body></s:Envelope>";
            int fillerLength = Math.Max(1, targetSizeBytes - header.Length - footer.Length);
            string payload = header + new string('A', fillerLength) + footer;

            return Encoding.UTF8.GetBytes(payload);
        }

        private static byte[] BuildTruncatedUtf8BomPayload()
        {
            byte[] truncatedBom = [0xEF, 0xBB];
            byte[] body = Encoding.UTF8.GetBytes(
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                "<s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>hello</echo></EchoString></s:Body>" +
                "</s:Envelope>");

            byte[] result = new byte[truncatedBom.Length + body.Length];
            Buffer.BlockCopy(truncatedBom, 0, result, 0, truncatedBom.Length);
            Buffer.BlockCopy(body, 0, result, truncatedBom.Length, body.Length);

            return result;
        }

        private static byte[] BuildUtf16LeBomWithUtf8BodyPayload()
        {
            byte[] utf16LeBom = [0xFF, 0xFE];
            byte[] body = Encoding.UTF8.GetBytes(
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                "<s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>hello</echo></EchoString></s:Body>" +
                "</s:Envelope>");

            byte[] result = new byte[utf16LeBom.Length + body.Length];
            Buffer.BlockCopy(utf16LeBom, 0, result, 0, utf16LeBom.Length);
            Buffer.BlockCopy(body, 0, result, utf16LeBom.Length, body.Length);

            return result;
        }

        #endregion
    }
}
