using System;
using System.IO.Compression;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using CloudNimble.Text.Xml.Serialization;

using FastSoap.Abstractions;
using FastSoap.Core.Buffers;
using FastSoap.Core.Dispatching;
using FastSoap.Core.Protocol;
using FastSoap.Core.Serialization;

using Xunit;

namespace FastSoap.ProtocolTests
{

    [XmlRoot(ElementName = "BusinessFaultDetail", Namespace = "urn:faults")]
    public sealed class BusinessFaultDetailWire
    {

        #region Properties

        [XmlElement(ElementName = "ErrorCode", Namespace = "urn:faults", Order = 0)]
        public string ErrorCode { get; set; } = string.Empty;

        [XmlElement(ElementName = "Retryable", Namespace = "urn:faults", Order = 1)]
        public bool Retryable { get; set; }

        #endregion

    }

    [XmlRoot(ElementName = "ThrowingSerializationWire", Namespace = "urn:faults")]
    public sealed class ThrowingSerializationWire
    {

        #region Properties

        [XmlElement(ElementName = "Value", Namespace = "urn:faults")]
        public string Value => throw new InvalidOperationException("SECRET_SERIALIZER_FAILURE");

        #endregion

    }

    [XmlSerializable<BusinessFaultDetailWire>]
    [XmlSerializable<ThrowingSerializationWire>]
    public partial class FaultProtocolXmlContext : XmlSerializerContext
    {
    }

    /// <summary>
    /// Verifies <see cref="SoapFaultException"/> handling, <see cref="SoapProcessingResult.UnhandledException"/>
    /// preservation across header/handler/serialization stages, and compressed request handling in <see cref="SoapMessageProcessor"/>.
    /// </summary>
    public sealed class SoapStructuredFaultAndExceptionLoggingTests
    {

        #region Fields

        private static readonly FaultProtocolXmlContext s_faultContext = new FaultProtocolXmlContext(
            XmlEssentialsAdapter.CreateStrictSerializerOptions());

        #endregion

        #region Public Methods

        [Fact]
        public async Task SoapMessageProcessor_CompressedRequestSupport_HandlesValidBombAndCorruptPayloads()
        {
            int invocations = 0;
            SoapEndpointOptions options = new SoapEndpointOptions
            {
                AllowCompressedRequests = true,
                EnabledVersions = SoapVersionFlags.Soap11,
                MaxRequestBytes = 1024
            };

            SoapOperationTableBuilder builder = new SoapOperationTableBuilder();
            builder.MapOperation(
                ProtocolTestFixtures.CreateEchoDescriptor(),
                ProtocolTestFixtures.Context.EchoStringRequestWire,
                ProtocolTestFixtures.Context.EchoStringResponseWire,
                (req, _, _) =>
                {
                    Interlocked.Increment(ref invocations);

                    return ValueTask.FromResult(new EchoStringResponseWire { EchoStringResult = req.Echo });
                });

            SoapMessageProcessor processor = new SoapMessageProcessor(options, builder.Build(options));
            byte[] rawXml = ProtocolTestFixtures.ReadFixtureBytes("soap11/echo/echo-dcs.request.xml");

            using BoundedPooledBufferWriter gzipWriter = new BoundedPooledBufferWriter(256, 4096);
            SoapCompressionCodec.Compress(rawXml, SoapCompressionEncoding.Gzip, gzipWriter);

            using (SoapProcessingResult okResult = await processor.ProcessAsync(
                new SoapRequestMetadata(SoapConstants.Soap11ResponseContentType, "\"http://tempuri.org/IEchoService/EchoString\"", ContentEncoding: "gzip"),
                gzipWriter.WrittenMemory,
                new FakeServiceProvider(),
                new ClaimsPrincipal(),
                TestContext.Current.CancellationToken))
            {
                Assert.Equal(200, okResult.StatusCode);
                Assert.True(okResult.ServiceInvoked);
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><s:Body><EchoStringResponse xmlns=\"http://tempuri.org/\"><EchoStringResult>hello</EchoStringResult></EchoStringResponse></s:Body></s:Envelope>",
                    Encoding.UTF8.GetString(okResult.Body.Span));
            }

            byte[] oversizedXml = Encoding.UTF8.GetBytes($"<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>{new string('Z', 2048)}</echo></EchoString></s:Body></s:Envelope>");
            using BoundedPooledBufferWriter bombWriter = new BoundedPooledBufferWriter(256, 4096);
            SoapCompressionCodec.Compress(oversizedXml, SoapCompressionEncoding.Brotli, bombWriter);

            Assert.True(bombWriter.WrittenCount < options.MaxRequestBytes);

            using (SoapProcessingResult bombResult = await processor.ProcessAsync(
                new SoapRequestMetadata(SoapConstants.Soap11ResponseContentType, "\"http://tempuri.org/IEchoService/EchoString\"", ContentEncoding: "br"),
                bombWriter.WrittenMemory,
                new FakeServiceProvider(),
                new ClaimsPrincipal(),
                TestContext.Current.CancellationToken))
            {
                Assert.Equal(413, bombResult.StatusCode);
                Assert.False(bombResult.ServiceInvoked);
            }

            using (SoapProcessingResult corruptResult = await processor.ProcessAsync(
                new SoapRequestMetadata(SoapConstants.Soap11ResponseContentType, "\"http://tempuri.org/IEchoService/EchoString\"", ContentEncoding: "deflate"),
                "corrupt"u8.ToArray(),
                new FakeServiceProvider(),
                new ClaimsPrincipal(),
                TestContext.Current.CancellationToken))
            {
                Assert.Equal(400, corruptResult.StatusCode);
                Assert.False(corruptResult.ServiceInvoked);
            }

            Assert.Equal(1, invocations);
        }

        [Fact]
        public async Task SoapMessageProcessor_SoapFaultExceptionInHandler_StagesSoap11AndSoap12Detail()
        {
            SoapEndpointOptions options = new SoapEndpointOptions { EnabledVersions = SoapVersionFlags.Both };
            SoapOperationTableBuilder builder = new SoapOperationTableBuilder();
            builder.MapOperation(
                ProtocolTestFixtures.CreateEchoDescriptor(),
                ProtocolTestFixtures.Context.EchoStringRequestWire,
                ProtocolTestFixtures.Context.EchoStringResponseWire,
                static (req, _, _) =>
                {
                    if (string.Equals(req.Echo, "typed-sender", StringComparison.Ordinal))
                    {
                        throw SoapFaultDetailSerializer.CreateFaultException(
                            SoapFaultKind.Sender,
                            "Business rule rejected request.",
                            new BusinessFaultDetailWire { ErrorCode = "E_LIMIT", Retryable = true },
                            s_faultContext.BusinessFaultDetailWire);
                    }

                    throw SoapFaultDetailSerializer.CreateFaultException(
                        SoapFaultKind.Receiver,
                        "Downstream fault with WCF detail.",
                        new SoapExceptionDetail
                        {
                            Message = "Downstream gateway timed out.",
                            Type = "System.TimeoutException"
                        },
                        actorOrNode: "urn:node:downstream");
                });

            SoapMessageProcessor processor = new SoapMessageProcessor(options, builder.Build(options));

            byte[] soap11Req = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>typed-sender</echo></EchoString></s:Body></s:Envelope>"u8.ToArray();
            using (SoapProcessingResult res11 = await processor.ProcessAsync(
                new SoapRequestMetadata(SoapConstants.Soap11ResponseContentType, "\"http://tempuri.org/IEchoService/EchoString\""),
                soap11Req,
                new FakeServiceProvider(),
                new ClaimsPrincipal(),
                TestContext.Current.CancellationToken))
            {
                Assert.True(res11.ServiceInvoked);
                Assert.Equal(500, res11.StatusCode);
                Assert.Equal(SoapFaultKind.Sender, res11.FaultKind);
                Assert.Null(res11.UnhandledException);
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault><faultcode>s:Client</faultcode><faultstring>Business rule rejected request.</faultstring><detail><BusinessFaultDetail xmlns=\"urn:faults\"><ErrorCode>E_LIMIT</ErrorCode><Retryable>true</Retryable></BusinessFaultDetail></detail></s:Fault></s:Body></s:Envelope>",
                    Encoding.UTF8.GetString(res11.Body.Span));
            }

            byte[] soap12SenderReq = "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>typed-sender</echo></EchoString></s:Body></s:Envelope>"u8.ToArray();
            using (SoapProcessingResult res12Sender = await processor.ProcessAsync(
                new SoapRequestMetadata("application/soap+xml; charset=utf-8; action=\"http://tempuri.org/IEchoService/EchoString\"", null),
                soap12SenderReq,
                new FakeServiceProvider(),
                new ClaimsPrincipal(),
                TestContext.Current.CancellationToken))
            {
                Assert.True(res12Sender.ServiceInvoked);
                Assert.Equal(400, res12Sender.StatusCode);
                Assert.Equal(SoapFaultKind.Sender, res12Sender.FaultKind);
                Assert.Null(res12Sender.UnhandledException);
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body><s:Fault><s:Code><s:Value>s:Sender</s:Value></s:Code><s:Reason><s:Text xml:lang=\"en-US\">Business rule rejected request.</s:Text></s:Reason><s:Detail><BusinessFaultDetail xmlns=\"urn:faults\"><ErrorCode>E_LIMIT</ErrorCode><Retryable>true</Retryable></BusinessFaultDetail></s:Detail></s:Fault></s:Body></s:Envelope>",
                    Encoding.UTF8.GetString(res12Sender.Body.Span));
            }

            byte[] soap12ReceiverReq = "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>wcf-receiver</echo></EchoString></s:Body></s:Envelope>"u8.ToArray();
            using (SoapProcessingResult res12Receiver = await processor.ProcessAsync(
                new SoapRequestMetadata("application/soap+xml; charset=utf-8; action=\"http://tempuri.org/IEchoService/EchoString\"", null),
                soap12ReceiverReq,
                new FakeServiceProvider(),
                new ClaimsPrincipal(),
                TestContext.Current.CancellationToken))
            {
                Assert.True(res12Receiver.ServiceInvoked);
                Assert.Equal(500, res12Receiver.StatusCode);
                Assert.Equal(SoapFaultKind.Receiver, res12Receiver.FaultKind);
                Assert.Null(res12Receiver.UnhandledException);
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body><s:Fault><s:Code><s:Value>s:Receiver</s:Value></s:Code><s:Reason><s:Text xml:lang=\"en-US\">Downstream fault with WCF detail.</s:Text></s:Reason><s:Node>urn:node:downstream</s:Node><s:Detail><ExceptionDetail xmlns=\"http://schemas.datacontract.org/2004/07/System.ServiceModel\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><HelpLink xsi:nil=\"true\"/><InnerException xsi:nil=\"true\"/><Message>Downstream gateway timed out.</Message><StackTrace xsi:nil=\"true\"/><Type>System.TimeoutException</Type></ExceptionDetail></s:Detail></s:Fault></s:Body></s:Envelope>",
                    Encoding.UTF8.GetString(res12Receiver.Body.Span));
            }
        }

        [Fact]
        public async Task SoapMessageProcessor_SoapFaultExceptionInHeaderHandler_StagesDetailWithoutInvokingService()
        {
            int serviceInvocations = 0;
            SoapEndpointOptions options = new SoapEndpointOptions { EnabledVersions = SoapVersionFlags.Soap12 };
            SoapOperationTableBuilder builder = new SoapOperationTableBuilder();
            builder.MapOperation(
                ProtocolTestFixtures.CreateEchoDescriptor(),
                ProtocolTestFixtures.Context.EchoStringRequestWire,
                ProtocolTestFixtures.Context.EchoStringResponseWire,
                (req, _, _) =>
                {
                    Interlocked.Increment(ref serviceInvocations);

                    return ValueTask.FromResult(new EchoStringResponseWire { EchoStringResult = req.Echo });
                });

            SoapHeaderProcessor headers = new SoapHeaderProcessor([new FaultingHeaderHandler(new SoapQName("urn:auth", "Token"))]);
            SoapMessageProcessor processor = new SoapMessageProcessor(options, builder.Build(options), headers);

            byte[] reqBytes = "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Header><a:Token xmlns:a=\"urn:auth\" s:mustUnderstand=\"true\">expired</a:Token></s:Header><s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>x</echo></EchoString></s:Body></s:Envelope>"u8.ToArray();
            using SoapProcessingResult result = await processor.ProcessAsync(
                new SoapRequestMetadata("application/soap+xml; charset=utf-8; action=\"http://tempuri.org/IEchoService/EchoString\"", null),
                reqBytes,
                new FakeServiceProvider(),
                new ClaimsPrincipal(),
                TestContext.Current.CancellationToken);

            Assert.Equal(0, serviceInvocations);
            Assert.False(result.ServiceInvoked);
            Assert.Equal(400, result.StatusCode);
            Assert.Equal(SoapFaultKind.Sender, result.FaultKind);
            Assert.Null(result.UnhandledException);
            Assert.Equal(
                "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body><s:Fault><s:Code><s:Value>s:Sender</s:Value></s:Code><s:Reason><s:Text xml:lang=\"en-US\">Authentication token expired.</s:Text></s:Reason><s:Detail><BusinessFaultDetail xmlns=\"urn:faults\"><ErrorCode>AUTH_EXPIRED</ErrorCode><Retryable>false</Retryable></BusinessFaultDetail></s:Detail></s:Fault></s:Body></s:Envelope>",
                Encoding.UTF8.GetString(result.Body.Span));
        }

        [Fact]
        public async Task SoapMessageProcessor_SoapFaultExceptionWithInvalidDetailXml_PopulatesUnhandledExceptionAndFaultStagingStage()
        {
            SoapEndpointOptions options = new SoapEndpointOptions { EnabledVersions = SoapVersionFlags.Soap11 };
            SoapOperationTableBuilder builder = new SoapOperationTableBuilder();
            builder.MapOperation(
                ProtocolTestFixtures.CreateEchoDescriptor(),
                ProtocolTestFixtures.Context.EchoStringRequestWire,
                ProtocolTestFixtures.Context.EchoStringResponseWire,
                static (_, _, _) => throw new SoapFaultException(
                    SoapFaultKind.Sender,
                    "Fault with unclosed detail element.",
                    "<Broken><Unclosed>"u8.ToArray()));

            SoapMessageProcessor processor = new SoapMessageProcessor(options, builder.Build(options));
            byte[] echoReq = ProtocolTestFixtures.ReadFixtureBytes("soap11/echo/echo-dcs.request.xml");

            using SoapProcessingResult result = await processor.ProcessAsync(
                new SoapRequestMetadata(SoapConstants.Soap11ResponseContentType, "\"http://tempuri.org/IEchoService/EchoString\""),
                echoReq,
                new FakeServiceProvider(),
                new ClaimsPrincipal(),
                TestContext.Current.CancellationToken);

            Assert.True(result.ServiceInvoked);
            Assert.Equal(500, result.StatusCode);
            Assert.Equal(SoapFaultKind.Sender, result.FaultKind);
            Assert.Null(result.ContentType);
            Assert.True(result.Body.IsEmpty);
            Assert.NotNull(result.UnhandledException);
            Assert.Equal("fault_staging", result.FailureStage);
        }

        [Fact]
        public async Task SoapMessageProcessor_UnhandledExceptions_PreserveExceptionAcrossHeaderHandlerAndSerialization()
        {
            SoapEndpointOptions options = new SoapEndpointOptions { EnabledVersions = SoapVersionFlags.Soap11 };

            InvalidOperationException headerException = new InvalidOperationException("SECRET_HEADER_KEY");
            SoapHeaderProcessor failingHeaders = new SoapHeaderProcessor([new ThrowingHeaderHandler(new SoapQName("urn:auth", "Token"), headerException)]);
            SoapOperationTableBuilder headerTestBuilder = new SoapOperationTableBuilder();
            headerTestBuilder.MapOperation(
                ProtocolTestFixtures.CreateEchoDescriptor(),
                ProtocolTestFixtures.Context.EchoStringRequestWire,
                ProtocolTestFixtures.Context.EchoStringResponseWire,
                static (req, _, _) => ValueTask.FromResult(new EchoStringResponseWire { EchoStringResult = req.Echo }));

            SoapMessageProcessor headerProcessor = new SoapMessageProcessor(options, headerTestBuilder.Build(options), failingHeaders);
            byte[] headerReq = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Header><a:Token xmlns:a=\"urn:auth\">x</a:Token></s:Header><s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>x</echo></EchoString></s:Body></s:Envelope>"u8.ToArray();

            using (SoapProcessingResult headerFault = await headerProcessor.ProcessAsync(
                new SoapRequestMetadata(SoapConstants.Soap11ResponseContentType, "\"http://tempuri.org/IEchoService/EchoString\""),
                headerReq,
                new FakeServiceProvider(),
                new ClaimsPrincipal(),
                TestContext.Current.CancellationToken))
            {
                Assert.False(headerFault.ServiceInvoked);
                Assert.Equal(500, headerFault.StatusCode);
                Assert.Equal(SoapFaultKind.Receiver, headerFault.FaultKind);
                Assert.Same(headerException, headerFault.UnhandledException);
                Assert.Equal("header_processing", headerFault.FailureStage);
                Assert.DoesNotContain("SECRET_HEADER_KEY", Encoding.UTF8.GetString(headerFault.Body.Span), StringComparison.Ordinal);
            }

            InvalidOperationException handlerException = new InvalidOperationException("SECRET_HANDLER_KEY");
            SoapOperationTableBuilder handlerTestBuilder = new SoapOperationTableBuilder();
            handlerTestBuilder.MapOperation(
                ProtocolTestFixtures.CreateEchoDescriptor(),
                ProtocolTestFixtures.Context.EchoStringRequestWire,
                ProtocolTestFixtures.Context.EchoStringResponseWire,
                (_, _, _) => throw handlerException);

            SoapMessageProcessor handlerProcessor = new SoapMessageProcessor(options, handlerTestBuilder.Build(options));
            byte[] echoReq = ProtocolTestFixtures.ReadFixtureBytes("soap11/echo/echo-dcs.request.xml");

            using (SoapProcessingResult handlerFault = await handlerProcessor.ProcessAsync(
                new SoapRequestMetadata(SoapConstants.Soap11ResponseContentType, "\"http://tempuri.org/IEchoService/EchoString\""),
                echoReq,
                new FakeServiceProvider(),
                new ClaimsPrincipal(),
                TestContext.Current.CancellationToken))
            {
                Assert.True(handlerFault.ServiceInvoked);
                Assert.Equal(500, handlerFault.StatusCode);
                Assert.Equal(SoapFaultKind.Receiver, handlerFault.FaultKind);
                Assert.Same(handlerException, handlerFault.UnhandledException);
                Assert.Equal("handler", handlerFault.FailureStage);
                Assert.DoesNotContain("SECRET_HANDLER_KEY", Encoding.UTF8.GetString(handlerFault.Body.Span), StringComparison.Ordinal);
            }

            SoapOperationDescriptor throwingSerDescriptor = new SoapOperationDescriptor(
                "ThrowSerialize",
                "urn:faults/ThrowSerialize",
                "urn:faults/ThrowSerializeResponse",
                new SoapQName("http://tempuri.org/", "EchoString"),
                new SoapQName("urn:faults", "ThrowingSerializationWire"));

            SoapOperationTableBuilder serializationTestBuilder = new SoapOperationTableBuilder();
            serializationTestBuilder.MapOperation(
                throwingSerDescriptor,
                ProtocolTestFixtures.Context.EchoStringRequestWire,
                s_faultContext.ThrowingSerializationWire,
                static (_, _, _) => ValueTask.FromResult(new ThrowingSerializationWire()));

            SoapMessageProcessor serializationProcessor = new SoapMessageProcessor(options, serializationTestBuilder.Build(options));

            using (SoapProcessingResult serializationFault = await serializationProcessor.ProcessAsync(
                new SoapRequestMetadata(SoapConstants.Soap11ResponseContentType, "\"urn:faults/ThrowSerialize\""),
                echoReq,
                new FakeServiceProvider(),
                new ClaimsPrincipal(),
                TestContext.Current.CancellationToken))
            {
                Assert.True(serializationFault.ServiceInvoked);
                Assert.Equal(500, serializationFault.StatusCode);
                Assert.Equal(SoapFaultKind.Receiver, serializationFault.FaultKind);
                Assert.NotNull(serializationFault.UnhandledException);
                Assert.Equal("SECRET_SERIALIZER_FAILURE", serializationFault.UnhandledException.Message);
                Assert.Equal("response_serialization", serializationFault.FailureStage);
                Assert.DoesNotContain("SECRET_SERIALIZER_FAILURE", Encoding.UTF8.GetString(serializationFault.Body.Span), StringComparison.Ordinal);
            }
        }

        #endregion

        private sealed class FaultingHeaderHandler : ISoapHeaderHandler
        {

            #region Properties

            public SoapQName HeaderName { get; }

            #endregion

            #region Constructors

            public FaultingHeaderHandler(SoapQName headerName)
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
                throw SoapFaultDetailSerializer.CreateFaultException(
                    SoapFaultKind.Sender,
                    "Authentication token expired.",
                    new BusinessFaultDetailWire { ErrorCode = "AUTH_EXPIRED", Retryable = false },
                    s_faultContext.BusinessFaultDetailWire);
            }

            #endregion

        }

        private sealed class ThrowingHeaderHandler : ISoapHeaderHandler
        {

            #region Fields

            private readonly Exception _exception;

            #endregion

            #region Properties

            public SoapQName HeaderName { get; }

            #endregion

            #region Constructors

            public ThrowingHeaderHandler(SoapQName headerName, Exception exception)
            {
                HeaderName = headerName;
                _exception = exception;
            }

            #endregion

            #region Public Methods

            public ValueTask HandleAsync(
                SoapHeaderValue header,
                SoapCallContext context,
                CancellationToken cancellationToken)
            {
                throw _exception;
            }

            #endregion

        }

    }

}
