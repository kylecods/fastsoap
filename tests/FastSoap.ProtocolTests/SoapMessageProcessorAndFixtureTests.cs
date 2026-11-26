using System;
using System.IO.Pipelines;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using FastSoap.Abstractions;
using FastSoap.Core.Buffers;
using FastSoap.Core.Dispatching;
using FastSoap.Core.Protocol;
using FastSoap.Core.Serialization;

using Xunit;

namespace FastSoap.ProtocolTests
{

    /// <summary>
    /// Verifies <see cref="SoapFaultWriter"/>, <see cref="SoapResponseWriter"/>, <see cref="SoapMessageProcessor"/>,
    /// and wire conformance against golden CoreWCF fixtures in <c>tests/fixtures/</c>.
    /// </summary>
    public sealed class SoapMessageProcessorAndFixtureTests
    {

        #region Public Methods

        [Theory]
        [InlineData("negative/neg01-dtd-xxe.request.xml", "\"http://tempuri.org/IEchoService/EchoString\"", 400, null, null)]
        [InlineData("negative/neg02-dtd-entity-expansion.request.xml", "\"http://tempuri.org/IEchoService/EchoString\"", 400, null, null)]
        [InlineData("negative/neg03a-malformed-utf8.request.bin", "\"http://tempuri.org/IEchoService/EchoString\"", 400, null, null)]
        [InlineData("negative/neg03b-invalid-xml10-char.request.bin", "\"http://tempuri.org/IEchoService/EchoString\"", 400, null, null)]
        [InlineData("negative/neg04-duplicate-body-soap11.request.xml", "\"http://tempuri.org/IEchoService/EchoString\"", 500, SoapFaultKind.Sender, "SOAP Envelope must contain only one Body element.")]
        [InlineData("negative/neg05-wrong-ordering-soap11.request.xml", "\"http://tempuri.org/IEchoService/EchoString\"", 500, SoapFaultKind.Sender, "SOAP Header must appear at most once and before Body.")]
        [InlineData("negative/neg06a-unknown-mustunderstand-soap11.request.xml", "\"http://tempuri.org/IEchoService/EchoString\"", 500, SoapFaultKind.MustUnderstand, "One or more mandatory SOAP headers were not understood.")]
        [InlineData("negative/neg07-action-body-mismatch-soap11.request.xml", "\"urn:payments/IPaymentService/Transfer\"", 500, SoapFaultKind.Sender, "SOAP action does not match the Body element qualified name.")]
        [InlineData("negative/neg10-non-envelope-root.request.xml", "\"urn:payments/IPaymentService/Transfer\"", 400, null, null)]
        [InlineData("negative/neg11-trailing-root.request.xml", "\"http://tempuri.org/IEchoService/EchoString\"", 400, null, null)]
        [InlineData("negative/neg12-missing-soapaction-soap11.request.xml", null, 500, SoapFaultKind.Sender, "Missing required SOAPAction header.")]
        [InlineData("negative/neg13-version-mismatch-soap12-on-soap11.request.xml", "\"http://tempuri.org/IEchoService/EchoString\"", 500, SoapFaultKind.VersionMismatch, "SOAP Envelope namespace does not match the endpoint SOAP version.")]
        public async Task GoldenNegativeFixtures_Soap11_RejectedWithZeroServiceInvocations(
            string fixtureFile,
            string? soapAction,
            int expectedStatus,
            SoapFaultKind? expectedFaultKind,
            string? expectedFaultString)
        {
            int invocations = 0;
            SoapEndpointOptions options = new SoapEndpointOptions { EnabledVersions = SoapVersionFlags.Soap11 };
            SoapMessageProcessor processor = CreateProcessor(options, () => Interlocked.Increment(ref invocations));
            byte[] requestBytes = ProtocolTestFixtures.ReadFixtureBytes(fixtureFile);

            using SoapProcessingResult result = await processor.ProcessAsync(
                new SoapRequestMetadata(SoapConstants.Soap11ResponseContentType, soapAction),
                requestBytes,
                new FakeServiceProvider(),
                new ClaimsPrincipal(),
                TestContext.Current.CancellationToken);

            Assert.Equal(0, invocations);
            Assert.False(result.ServiceInvoked);
            Assert.Equal(expectedStatus, result.StatusCode);
            Assert.Equal(expectedFaultKind, result.FaultKind);

            if (expectedFaultKind is not null)
            {
                Assert.Equal(SoapConstants.Soap11ResponseContentType, result.ContentType);
                string bodyText = Encoding.UTF8.GetString(result.Body.Span);
                string expectedFaultCode = expectedFaultKind.Value switch
                {
                    SoapFaultKind.Sender => "s:Client",
                    SoapFaultKind.VersionMismatch => "s:VersionMismatch",
                    SoapFaultKind.MustUnderstand => "s:MustUnderstand",
                    _ => "s:Server"
                };
                string expectedXml = $"<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault><faultcode>{expectedFaultCode}</faultcode><faultstring>{expectedFaultString}</faultstring></s:Fault></s:Body></s:Envelope>";
                Assert.Equal(expectedXml, bodyText);
            }
        }

        [Theory]
        [InlineData("<?xml version=\"1.0\" encoding=\"utf-8\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><broken")]
        [InlineData("<?pi?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"")]
        [InlineData("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>x</echo><?xml foo?></EchoString></s:Body></s:Envelope>")]
        public async Task SoapMessageProcessor_MalformedXmlWithPiMarkerOrBodyEmbeddedXmlPi_Returns400Not500(string malformedXml)
        {
            int invocations = 0;
            SoapEndpointOptions options = new SoapEndpointOptions { EnabledVersions = SoapVersionFlags.Soap11 };
            SoapMessageProcessor processor = CreateProcessor(options, () => Interlocked.Increment(ref invocations));

            using SoapProcessingResult result = await processor.ProcessAsync(
                new SoapRequestMetadata(SoapConstants.Soap11ResponseContentType, "\"http://tempuri.org/IEchoService/EchoString\""),
                Encoding.UTF8.GetBytes(malformedXml),
                new FakeServiceProvider(),
                new ClaimsPrincipal(),
                TestContext.Current.CancellationToken);

            Assert.Equal(0, invocations);
            Assert.False(result.ServiceInvoked);
            Assert.Equal(400, result.StatusCode);
            Assert.Null(result.FaultKind);
            Assert.True(result.Body.IsEmpty);
        }

        [Fact]
        public async Task GoldenNegativeFixtures_Neg06B_Neg08_Neg09_EnforceExactResponses()
        {
            int invocations = 0;
            SoapEndpointOptions soap12Options = new SoapEndpointOptions { EnabledVersions = SoapVersionFlags.Soap12 };
            SoapMessageProcessor soap12Processor = CreateProcessor(soap12Options, () => Interlocked.Increment(ref invocations));

            byte[] neg06b = ProtocolTestFixtures.ReadFixtureBytes("negative/neg06b-unknown-mustunderstand-soap12.request.xml");
            using (SoapProcessingResult res06b = await soap12Processor.ProcessAsync(
                new SoapRequestMetadata("application/soap+xml; charset=utf-8; action=\"http://tempuri.org/IEchoService/EchoString\"", null),
                neg06b,
                new FakeServiceProvider(),
                new ClaimsPrincipal(),
                TestContext.Current.CancellationToken))
            {
                Assert.Equal(0, invocations);
                Assert.Equal(500, res06b.StatusCode);
                Assert.Equal(SoapConstants.Soap12ResponseContentType, res06b.ContentType);
                Assert.Equal(SoapFaultKind.MustUnderstand, res06b.FaultKind);
                string xml = Encoding.UTF8.GetString(res06b.Body.Span);
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Header><s:NotUnderstood xmlns:h0=\"urn:unknown\" qname=\"h0:UnknownMandatory\"/></s:Header><s:Body><s:Fault><s:Code><s:Value>s:MustUnderstand</s:Value></s:Code><s:Reason><s:Text xml:lang=\"en-US\">One or more mandatory SOAP headers were not understood.</s:Text></s:Reason></s:Fault></s:Body></s:Envelope>",
                    xml);
            }

            SoapEndpointOptions quotaOptions = new SoapEndpointOptions
            {
                EnabledVersions = SoapVersionFlags.Soap11,
                MaxRequestBytes = 65536
            };
            SoapMessageProcessor quotaProcessor = CreateProcessor(quotaOptions, () => Interlocked.Increment(ref invocations));

            byte[] neg08 = ProtocolTestFixtures.ReadFixtureBytes("negative/neg08-oversized-payload-soap11.request.xml");
            using (SoapProcessingResult res08 = await quotaProcessor.ProcessAsync(
                new SoapRequestMetadata(SoapConstants.Soap11ResponseContentType, "\"http://tempuri.org/IEchoService/EchoString\""),
                neg08,
                new FakeServiceProvider(),
                new ClaimsPrincipal(),
                TestContext.Current.CancellationToken))
            {
                Assert.Equal(0, invocations);
                Assert.Equal(413, res08.StatusCode);
                Assert.True(res08.Body.IsEmpty);
            }

            byte[] neg09 = ProtocolTestFixtures.ReadFixtureBytes("negative/neg09-utf16-encoding-mismatch.request.xml");
            using (SoapProcessingResult res09 = await quotaProcessor.ProcessAsync(
                new SoapRequestMetadata("text/xml; charset=utf-16", "\"http://tempuri.org/IEchoService/EchoString\""),
                neg09,
                new FakeServiceProvider(),
                new ClaimsPrincipal(),
                TestContext.Current.CancellationToken))
            {
                Assert.Equal(0, invocations);
                Assert.Equal(415, res09.StatusCode);
                Assert.True(res09.Body.IsEmpty);
            }
        }

        [Fact]
        public async Task GoldenPositiveFixtures_Soap11AndSoap12_ProduceExactExpectedWireResponses()
        {
            int invocations = 0;
            SoapEndpointOptions options = new SoapEndpointOptions { EnabledVersions = SoapVersionFlags.Both };
            RecordingHeaderHandler apiKeyHandler = new RecordingHeaderHandler(new SoapQName("http://tempuri.org/", "ApiKey"));
            SoapMessageProcessor processor = CreateProcessor(
                options,
                () => Interlocked.Increment(ref invocations),
                new SoapHeaderProcessor([apiKeyHandler]));

            byte[] echo11Req = ProtocolTestFixtures.ReadFixtureBytes("soap11/echo/echo-dcs.request.xml");
            using (SoapProcessingResult echo11Res = await processor.ProcessAsync(
                new SoapRequestMetadata(SoapConstants.Soap11ResponseContentType, "\"http://tempuri.org/IEchoService/EchoString\""),
                echo11Req,
                new FakeServiceProvider(),
                new ClaimsPrincipal(),
                TestContext.Current.CancellationToken))
            {
                Assert.Equal(200, echo11Res.StatusCode);
                Assert.Equal(SoapConstants.Soap11ResponseContentType, echo11Res.ContentType);
                Assert.True(echo11Res.ServiceInvoked);
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><EchoStringResponse xmlns=\"http://tempuri.org/\"><EchoStringResult>hello</EchoStringResult></EchoStringResponse></s:Body></s:Envelope>",
                    Encoding.UTF8.GetString(echo11Res.Body.Span));
            }

            byte[] nonAsciiReq = ProtocolTestFixtures.ReadFixtureBytes("soap11/echo/echo-dcs-nonascii.request.xml");
            using (SoapProcessingResult nonAsciiRes = await processor.ProcessAsync(
                new SoapRequestMetadata(SoapConstants.Soap11ResponseContentType, "\"http://tempuri.org/IEchoService/EchoString\""),
                nonAsciiReq,
                new FakeServiceProvider(),
                new ClaimsPrincipal(),
                TestContext.Current.CancellationToken))
            {
                Assert.Equal(200, nonAsciiRes.StatusCode);
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><EchoStringResponse xmlns=\"http://tempuri.org/\"><EchoStringResult>Hello, 世界! € — café 🚀</EchoStringResult></EchoStringResponse></s:Body></s:Envelope>",
                    Encoding.UTF8.GetString(nonAsciiRes.Body.Span));
            }

            byte[] nilReq = ProtocolTestFixtures.ReadFixtureBytes("soap11/echo/echo-dcs-null.request.xml");
            using (SoapProcessingResult nilRes = await processor.ProcessAsync(
                new SoapRequestMetadata(SoapConstants.Soap11ResponseContentType, "\"http://tempuri.org/IEchoService/EchoString\""),
                nilReq,
                new FakeServiceProvider(),
                new ClaimsPrincipal(),
                TestContext.Current.CancellationToken))
            {
                Assert.Equal(200, nilRes.StatusCode);
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><EchoStringResponse xmlns=\"http://tempuri.org/\"><EchoStringResult xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:nil=\"true\"/></EchoStringResponse></s:Body></s:Envelope>",
                    Encoding.UTF8.GetString(nilRes.Body.Span));
            }

            byte[] headerReq = ProtocolTestFixtures.ReadFixtureBytes("soap11/headers/echo-header-optional.request.xml");
            using (SoapProcessingResult headerRes = await processor.ProcessAsync(
                new SoapRequestMetadata(SoapConstants.Soap11ResponseContentType, "\"http://tempuri.org/IServiceWithMessageBodyAndHeader/EchoWithMessageContract\""),
                headerReq,
                new FakeServiceProvider(),
                new ClaimsPrincipal(),
                TestContext.Current.CancellationToken))
            {
                Assert.Equal(200, headerRes.StatusCode);
                Assert.Equal(1, apiKeyHandler.InvocationCount);
                Assert.Equal(
                    "<h:ApiKey xmlns:h=\"http://tempuri.org/\" xmlns=\"http://tempuri.org/\">DEVKEYTOTEST</h:ApiKey>",
                    Encoding.UTF8.GetString(apiKeyHandler.HandledHeaders[0].RawUtf8Payload.Span));
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><EchoHeaderResponse xmlns=\"http://tempuri.org/\"><SayHello>Saying Hello Message Hello</SayHello></EchoHeaderResponse></s:Body></s:Envelope>",
                    Encoding.UTF8.GetString(headerRes.Body.Span));
            }

            byte[] transfer11Req = ProtocolTestFixtures.ReadFixtureBytes("soap11/transfer/transfer-xmlser-urnpayments.request.xml");
            using (SoapProcessingResult transfer11Res = await processor.ProcessAsync(
                new SoapRequestMetadata(SoapConstants.Soap11ResponseContentType, "\"urn:payments/IPaymentService/Transfer\""),
                transfer11Req,
                new FakeServiceProvider(),
                new ClaimsPrincipal(),
                TestContext.Current.CancellationToken))
            {
                Assert.Equal(200, transfer11Res.StatusCode);
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><TransferResponse xmlns=\"urn:payments\"><TransferResult><TransactionId>TX-001</TransactionId><Successful>true</Successful><ProcessedAtUtc>2026-11-26T12:00:00Z</ProcessedAtUtc><Status>Completed</Status></TransferResult></TransferResponse></s:Body></s:Envelope>",
                    Encoding.UTF8.GetString(transfer11Res.Body.Span));
            }

            byte[] echo12Req = ProtocolTestFixtures.ReadFixtureBytes("soap12/echo/echo-dcs.request.xml");
            using (SoapProcessingResult echo12Res = await processor.ProcessAsync(
                new SoapRequestMetadata("application/soap+xml; charset=utf-8; action=\"http://tempuri.org/IEchoService/EchoString\"", null),
                echo12Req,
                new FakeServiceProvider(),
                new ClaimsPrincipal(),
                TestContext.Current.CancellationToken))
            {
                Assert.Equal(200, echo12Res.StatusCode);
                Assert.Equal(SoapConstants.Soap12ResponseContentType, echo12Res.ContentType);
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body><EchoStringResponse xmlns=\"http://tempuri.org/\"><EchoStringResult>hello</EchoStringResult></EchoStringResponse></s:Body></s:Envelope>",
                    Encoding.UTF8.GetString(echo12Res.Body.Span));
            }

            byte[] transfer12Req = ProtocolTestFixtures.ReadFixtureBytes("soap12/transfer/transfer-xmlser-urnpayments.request.xml");
            using (SoapProcessingResult transfer12Res = await processor.ProcessAsync(
                new SoapRequestMetadata("application/soap+xml; charset=utf-8; action=\"urn:payments/IPaymentService/Transfer\"", null),
                transfer12Req,
                new FakeServiceProvider(),
                new ClaimsPrincipal(),
                TestContext.Current.CancellationToken))
            {
                Assert.Equal(200, transfer12Res.StatusCode);
                Assert.Equal(SoapConstants.Soap12ResponseContentType, transfer12Res.ContentType);
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body><TransferResponse xmlns=\"urn:payments\"><TransferResult><TransactionId>TX-001</TransactionId><Successful>true</Successful><ProcessedAtUtc>2026-11-26T12:00:00Z</ProcessedAtUtc><Status>Completed</Status></TransferResult></TransferResponse></s:Body></s:Envelope>",
                    Encoding.UTF8.GetString(transfer12Res.Body.Span));
            }

            Assert.Equal(7, invocations);
        }

        [Fact]
        public async Task SoapMessageProcessor_UnhandledException_NeverLeaksRawExceptionMessage()
        {
            SoapEndpointOptions options = new SoapEndpointOptions { EnabledVersions = SoapVersionFlags.Both };
            SoapOperationTableBuilder builder = new SoapOperationTableBuilder();
            builder.MapOperation(
                ProtocolTestFixtures.CreateEchoDescriptor(),
                ProtocolTestFixtures.Context.EchoStringRequestWire,
                ProtocolTestFixtures.Context.EchoStringResponseWire,
                static (_, _, _) => throw new InvalidOperationException("SECRET_CONNECTION_STRING_PASSWORD"));

            SoapMessageProcessor processor = new SoapMessageProcessor(options, builder.Build(options));
            byte[] req = ProtocolTestFixtures.ReadFixtureBytes("soap12/echo/echo-dcs.request.xml");

            using SoapProcessingResult result = await processor.ProcessAsync(
                new SoapRequestMetadata("application/soap+xml; charset=utf-8; action=\"http://tempuri.org/IEchoService/EchoString\"", null),
                req,
                new FakeServiceProvider(),
                new ClaimsPrincipal(),
                TestContext.Current.CancellationToken);

            Assert.True(result.ServiceInvoked);
            Assert.Equal(500, result.StatusCode);
            Assert.Equal(SoapFaultKind.Receiver, result.FaultKind);
            string xml = Encoding.UTF8.GetString(result.Body.Span);
            Assert.DoesNotContain("SECRET_CONNECTION_STRING_PASSWORD", xml, StringComparison.Ordinal);
            Assert.Equal(
                "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body><s:Fault><s:Code><s:Value>s:Receiver</s:Value></s:Code><s:Reason><s:Text xml:lang=\"en-US\">An internal server error occurred while processing the SOAP request.</s:Text></s:Reason></s:Fault></s:Body></s:Envelope>",
                xml);
        }

        [Fact]
        public void SoapFaultWriter_WritesExactSoap11AndSoap12FaultsWithActorNodeRoleDetail()
        {
            byte[] detail = "<PaymentFaultDetail xmlns=\"urn:payments\"><ErrorCode>INVALID_AMOUNT</ErrorCode></PaymentFaultDetail>"u8.ToArray();

            SoapFault soap11Fault = new SoapFault(
                SoapVersion.Soap11,
                SoapFaultKind.Sender,
                "urn:custom",
                "InvalidInput",
                "Validation failed.",
                actorOrNode: "urn:node:payments",
                detailXmlUtf8: detail);

            using (BoundedPooledBufferWriter writer11 = new BoundedPooledBufferWriter(256, 4096))
            {
                SoapFaultWriter.WriteFault(soap11Fault, writer11);
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault><faultcode xmlns:fc=\"urn:custom\">fc:InvalidInput</faultcode><faultstring>Validation failed.</faultstring><faultactor>urn:node:payments</faultactor><detail><PaymentFaultDetail xmlns=\"urn:payments\"><ErrorCode>INVALID_AMOUNT</ErrorCode></PaymentFaultDetail></detail></s:Fault></s:Body></s:Envelope>",
                    Encoding.UTF8.GetString(writer11.WrittenSpan));
            }

            SoapFault soap12Fault = SoapFault.CreateStandard(
                SoapVersion.Soap12,
                SoapFaultKind.Sender,
                "Validation failed.",
                actorOrNode: "urn:node:payments",
                detailXmlUtf8: detail);

            using (BoundedPooledBufferWriter writer12 = new BoundedPooledBufferWriter(256, 4096))
            {
                SoapFaultWriter.WriteFault(soap12Fault, writer12, soap12Role: SoapConstants.Soap12UltimateReceiverRoleUri);
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body><s:Fault><s:Code><s:Value>s:Sender</s:Value></s:Code><s:Reason><s:Text xml:lang=\"en-US\">Validation failed.</s:Text></s:Reason><s:Node>urn:node:payments</s:Node><s:Role>http://www.w3.org/2003/05/soap-envelope/role/ultimateReceiver</s:Role><s:Detail><PaymentFaultDetail xmlns=\"urn:payments\"><ErrorCode>INVALID_AMOUNT</ErrorCode></PaymentFaultDetail></s:Detail></s:Fault></s:Body></s:Envelope>",
                    Encoding.UTF8.GetString(writer12.WrittenSpan));
            }
        }

        [Fact]
        public async Task SoapMessageProcessor_CommitToAsync_StreamsToPipeAndUnsupportedContentEncodingReturns415()
        {
            SoapEndpointOptions options = new SoapEndpointOptions { EnabledVersions = SoapVersionFlags.Soap11 };
            SoapMessageProcessor processor = CreateProcessor(options, static () => { });
            byte[] req = ProtocolTestFixtures.ReadFixtureBytes("soap11/echo/echo-dcs.request.xml");

            using SoapProcessingResult encodingErr = await processor.ProcessAsync(
                new SoapRequestMetadata(SoapConstants.Soap11ResponseContentType, "\"http://tempuri.org/IEchoService/EchoString\"", ContentEncoding: "gzip"),
                req,
                new FakeServiceProvider(),
                new ClaimsPrincipal(),
                TestContext.Current.CancellationToken);

            Assert.Equal(415, encodingErr.StatusCode);
            Assert.False(encodingErr.ServiceInvoked);

            Pipe pipe = new Pipe();
            using SoapProcessingResult okResult = await processor.ProcessAsync(
                new SoapRequestMetadata(SoapConstants.Soap11ResponseContentType, "\"http://tempuri.org/IEchoService/EchoString\""),
                req,
                new FakeServiceProvider(),
                new ClaimsPrincipal(),
                TestContext.Current.CancellationToken);

            await okResult.CommitToAsync(pipe.Writer, TestContext.Current.CancellationToken);
            await pipe.Writer.CompleteAsync();

            using SoapRequestBuffer readBack = await SoapRequestBuffer.CopyFromPipeAsync(
                pipe.Reader,
                maxBytes: 4096,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(readBack.Span.SequenceEqual(okResult.Body.Span));
        }

        [Fact]
        public async Task SoapMessageProcessor_PostEnvelopeSchemaViolation_MapsToSoapFaultPerMatrix()
        {
            int invocations = 0;
            SoapEndpointOptions options = new SoapEndpointOptions { EnabledVersions = SoapVersionFlags.Both };
            SoapMessageProcessor processor = CreateProcessor(options, () => Interlocked.Increment(ref invocations));

            byte[] soap11Unmapped = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>a</echo><Extra>b</Extra></EchoString></s:Body></s:Envelope>"u8.ToArray();
            using (SoapProcessingResult res11 = await processor.ProcessAsync(
                new SoapRequestMetadata(SoapConstants.Soap11ResponseContentType, "\"http://tempuri.org/IEchoService/EchoString\""),
                soap11Unmapped,
                new FakeServiceProvider(),
                new ClaimsPrincipal(),
                TestContext.Current.CancellationToken))
            {
                Assert.Equal(0, invocations);
                Assert.Equal(500, res11.StatusCode);
                Assert.Equal(SoapFaultKind.Sender, res11.FaultKind);
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault><faultcode>s:Client</faultcode><faultstring>SOAP request payload failed schema or type validation.</faultstring></s:Fault></s:Body></s:Envelope>",
                    Encoding.UTF8.GetString(res11.Body.Span));
            }

            byte[] soap12Duplicate = "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>a</echo><echo>b</echo></EchoString></s:Body></s:Envelope>"u8.ToArray();
            using (SoapProcessingResult res12 = await processor.ProcessAsync(
                new SoapRequestMetadata("application/soap+xml; charset=utf-8; action=\"http://tempuri.org/IEchoService/EchoString\"", null),
                soap12Duplicate,
                new FakeServiceProvider(),
                new ClaimsPrincipal(),
                TestContext.Current.CancellationToken))
            {
                Assert.Equal(0, invocations);
                Assert.Equal(400, res12.StatusCode);
                Assert.Equal(SoapFaultKind.Sender, res12.FaultKind);
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body><s:Fault><s:Code><s:Value>s:Sender</s:Value></s:Code><s:Reason><s:Text xml:lang=\"en-US\">SOAP request payload failed schema or type validation.</s:Text></s:Reason></s:Fault></s:Body></s:Envelope>",
                    Encoding.UTF8.GetString(res12.Body.Span));
            }
        }

        #endregion

        #region Private Methods

        private static SoapMessageProcessor CreateProcessor(
            SoapEndpointOptions options,
            Action onInvoke,
            SoapHeaderProcessor? headerProcessor = null)
        {
            SoapOperationTableBuilder builder = new SoapOperationTableBuilder();

            builder.MapOperation(
                ProtocolTestFixtures.CreateEchoDescriptor(),
                ProtocolTestFixtures.Context.EchoStringRequestWire,
                ProtocolTestFixtures.Context.EchoStringResponseWire,
                (req, _, _) =>
                {
                    onInvoke();

                    return ValueTask.FromResult(new EchoStringResponseWire { EchoStringResult = req.Echo });
                });

            builder.MapOperation(
                ProtocolTestFixtures.CreateEchoHeaderDescriptor(),
                ProtocolTestFixtures.Context.EchoHeaderRequestWire,
                ProtocolTestFixtures.Context.EchoHeaderResponseWire,
                (req, _, _) =>
                {
                    onInvoke();

                    return ValueTask.FromResult(new EchoHeaderResponseWire { SayHello = $"Saying Hello {req.Text}" });
                });

            builder.MapOperation(
                ProtocolTestFixtures.CreateTransferDescriptor(),
                ProtocolTestFixtures.Context.TransferRequestWire,
                ProtocolTestFixtures.Context.TransferResponseWire,
                (_, _, _) =>
                {
                    onInvoke();

                    return ValueTask.FromResult(new TransferResponseWire
                    {
                        TransferResult = new TransferResultDto
                        {
                            TransactionId = "TX-001",
                            Successful = true,
                            ProcessedAtUtc = "2026-11-26T12:00:00Z",
                            Status = "Completed"
                        }
                    });
                });

            return new SoapMessageProcessor(options, builder.Build(options), headerProcessor);
        }

        #endregion

    }

}
