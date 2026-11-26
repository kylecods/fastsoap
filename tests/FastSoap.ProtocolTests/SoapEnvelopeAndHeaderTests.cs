using System;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

using FastSoap.Abstractions;
using FastSoap.Core.Dispatching;
using FastSoap.Core.Protocol;
using FastSoap.Core.Serialization;

using Xunit;

namespace FastSoap.ProtocolTests
{

    /// <summary>
    /// Verifies SOAP 1.1 and SOAP 1.2 envelope grammar, quotas, QName routing, and header/actor/role semantics.
    /// </summary>
    public sealed class SoapEnvelopeAndHeaderTests
    {

        #region Public Methods

        [Fact]
        public async Task ParseAndPrepare_ActionlessRouting_SupportedOnlyWhenEnabled()
        {
            SoapOperationTableBuilder builder = new SoapOperationTableBuilder();
            builder.MapOperation(
                ProtocolTestFixtures.CreateEchoDescriptor(),
                ProtocolTestFixtures.Context.EchoStringRequestWire,
                ProtocolTestFixtures.Context.EchoStringResponseWire,
                static (req, _, _) => ValueTask.FromResult(new EchoStringResponseWire { EchoStringResult = req.Echo }));

            SoapEndpointOptions soap12Options = new SoapEndpointOptions { EnabledVersions = SoapVersionFlags.Soap12 };
            SoapOperationTable soap12Table = builder.Build(soap12Options);

            byte[] soap12Bytes = "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>actionless</echo></EchoString></s:Body></s:Envelope>"u8.ToArray();
            PreparedSoapCall call = SoapEnvelopeReader.ParseAndPrepare(
                soap12Bytes,
                SoapVersion.Soap12,
                action: null,
                soap12Table,
                soap12Options,
                new FakeServiceProvider(),
                new ClaimsPrincipal());

            Assert.Equal("http://tempuri.org/IEchoService/EchoString", call.Context.Action);

            SoapEndpointOptions soap11Strict = new SoapEndpointOptions { EnabledVersions = SoapVersionFlags.Soap11, AllowMissingSoap11Action = false };
            SoapOperationTable strictTable = builder.Build(soap11Strict);
            byte[] soap11Bytes = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>strict</echo></EchoString></s:Body></s:Envelope>"u8.ToArray();

            SoapProtocolException strictEx = Assert.Throws<SoapProtocolException>(() =>
                SoapEnvelopeReader.ParseAndPrepare(
                    soap11Bytes,
                    SoapVersion.Soap11,
                    action: null,
                    strictTable,
                    soap11Strict,
                    new FakeServiceProvider(),
                    new ClaimsPrincipal()));

            Assert.Equal(SoapFaultKind.Sender, strictEx.FaultKind);
            Assert.Equal(HttpStatusCode.InternalServerError, strictEx.HttpStatusCode);
            Assert.Equal(SoapVersion.Soap11, strictEx.SoapVersion);
        }

        [Fact]
        public void ParseAndPrepare_DistinguishesNonEnvelopeRootFromVersionMismatch()
        {
            SoapEndpointOptions options = new SoapEndpointOptions { EnabledVersions = SoapVersionFlags.Both };
            SoapOperationTable table = CreateEchoTable(options);

            byte[] nonEnvelope = "<Transfer xmlns=\"urn:payments\"><request /></Transfer>"u8.ToArray();
            SoapProtocolException nonEnvEx = Assert.Throws<SoapProtocolException>(() =>
                SoapEnvelopeReader.ParseAndPrepare(
                    nonEnvelope,
                    SoapVersion.Soap11,
                    "http://tempuri.org/IEchoService/EchoString",
                    table,
                    options,
                    new FakeServiceProvider(),
                    new ClaimsPrincipal()));

            Assert.Equal(HttpStatusCode.BadRequest, nonEnvEx.HttpStatusCode);
            Assert.Null(nonEnvEx.SoapVersion);

            byte[] wrongNsEnvelope = "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>x</echo></EchoString></s:Body></s:Envelope>"u8.ToArray();
            SoapProtocolException versionEx = Assert.Throws<SoapProtocolException>(() =>
                SoapEnvelopeReader.ParseAndPrepare(
                    wrongNsEnvelope,
                    SoapVersion.Soap11,
                    "http://tempuri.org/IEchoService/EchoString",
                    table,
                    options,
                    new FakeServiceProvider(),
                    new ClaimsPrincipal()));

            Assert.Equal(SoapFaultKind.VersionMismatch, versionEx.FaultKind);
            Assert.Equal(HttpStatusCode.InternalServerError, versionEx.HttpStatusCode);
            Assert.Equal(SoapVersion.Soap11, versionEx.SoapVersion);
        }

        [Theory]
        [InlineData("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>x</echo></EchoString></s:Body></s:Envelope>", SoapVersion.Soap11, HttpStatusCode.InternalServerError)]
        [InlineData("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><EchoString xmlns=\"http://tempuri.org/\"><echo>x</echo></EchoString></s:Body></s:Envelope>", SoapVersion.Soap11, HttpStatusCode.InternalServerError)]
        [InlineData("<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\" s:encodingStyle=\"http://www.w3.org/2003/05/soap-encoding\"><s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>x</echo></EchoString></s:Body></s:Envelope>", SoapVersion.Soap12, HttpStatusCode.BadRequest)]
        [InlineData("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Header><x:H xmlns:x=\"urn:x\" s:mustUnderstand=\"true\" /></s:Header><s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>x</echo></EchoString></s:Body></s:Envelope>", SoapVersion.Soap11, HttpStatusCode.InternalServerError)]
        [InlineData("<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Header><x:H xmlns:x=\"urn:x\" s:mustUnderstand=\"TRUE\" /></s:Header><s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>x</echo></EchoString></s:Body></s:Envelope>", SoapVersion.Soap12, HttpStatusCode.BadRequest)]
        [InlineData("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>x</echo></EchoString><EchoString xmlns=\"http://tempuri.org/\"><echo>y</echo></EchoString></s:Body></s:Envelope>", SoapVersion.Soap11, HttpStatusCode.InternalServerError)]
        [InlineData("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body /></s:Envelope>", SoapVersion.Soap11, HttpStatusCode.InternalServerError)]
        [InlineData("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" />", SoapVersion.Soap11, HttpStatusCode.InternalServerError)]
        public void ParseAndPrepare_EnvelopeGrammarAndEncodingStyleViolations_ThrowExpectedSenderFault(
            string xml,
            SoapVersion version,
            HttpStatusCode expectedStatus)
        {
            SoapEndpointOptions options = new SoapEndpointOptions { EnabledVersions = SoapVersionFlags.Both };
            SoapOperationTable table = CreateEchoTable(options);

            SoapProtocolException ex = Assert.Throws<SoapProtocolException>(() =>
                SoapEnvelopeReader.ParseAndPrepare(
                    Encoding.UTF8.GetBytes(xml),
                    version,
                    "http://tempuri.org/IEchoService/EchoString",
                    table,
                    options,
                    new FakeServiceProvider(),
                    new ClaimsPrincipal()));

            Assert.Equal(SoapFaultKind.Sender, ex.FaultKind);
            Assert.Equal(expectedStatus, ex.HttpStatusCode);
            Assert.Equal(version, ex.SoapVersion);
        }

        [Fact]
        public void ParseAndPrepare_HeaderCountAndHeaderBytesQuotas_EnforcedBeforeDeserialization()
        {
            SoapEndpointOptions countLimited = new SoapEndpointOptions
            {
                EnabledVersions = SoapVersionFlags.Soap11,
                MaxHeaderCount = 1
            };
            SoapOperationTable table = CreateEchoTable(countLimited);

            byte[] twoHeaders = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Header><h:A xmlns:h=\"urn:h\">1</h:A><h:B xmlns:h=\"urn:h\">2</h:B></s:Header><s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>x</echo></EchoString></s:Body></s:Envelope>"u8.ToArray();

            SoapProtocolException countEx = Assert.Throws<SoapProtocolException>(() =>
                SoapEnvelopeReader.ParseAndPrepare(
                    twoHeaders,
                    SoapVersion.Soap11,
                    "http://tempuri.org/IEchoService/EchoString",
                    table,
                    countLimited,
                    new FakeServiceProvider(),
                    new ClaimsPrincipal()));

            Assert.Equal(SoapFaultKind.Sender, countEx.FaultKind);
            Assert.Equal(HttpStatusCode.InternalServerError, countEx.HttpStatusCode);

            SoapEndpointOptions bytesLimited = new SoapEndpointOptions
            {
                EnabledVersions = SoapVersionFlags.Soap11,
                MaxHeaderBytes = 20
            };

            SoapProtocolException bytesEx = Assert.Throws<SoapProtocolException>(() =>
                SoapEnvelopeReader.ParseAndPrepare(
                    twoHeaders,
                    SoapVersion.Soap11,
                    "http://tempuri.org/IEchoService/EchoString",
                    table,
                    bytesLimited,
                    new FakeServiceProvider(),
                    new ClaimsPrincipal()));

            Assert.Equal(SoapFaultKind.Sender, bytesEx.FaultKind);
        }

        [Theory]
        [InlineData("<?xml version=\"1.1\" encoding=\"utf-8\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>x</echo></EchoString></s:Body></s:Envelope>")]
        [InlineData("<?pi before-root?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>x</echo></EchoString></s:Body></s:Envelope>")]
        [InlineData("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Header><?pi inside-header?></s:Header><s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>x</echo></EchoString></s:Body></s:Envelope>")]
        [InlineData("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><?pi inside-body?><EchoString xmlns=\"http://tempuri.org/\"><echo>x</echo></EchoString></s:Body></s:Envelope>")]
        [InlineData("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>x</echo><?pi inside-dto?></EchoString></s:Body></s:Envelope>")]
        [InlineData("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>x</echo><?xml foo?></EchoString></s:Body></s:Envelope>")]
        [InlineData("<?xml version=\"1.0\" encoding=\"utf-8\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>x</echo><?xml foo?></EchoString></s:Body></s:Envelope>")]
        [InlineData("<?xml version=\"1.0\" encoding=\"utf-8\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><broken")]
        [InlineData("<?pi?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"")]
        public void ParseAndPrepare_Xml11AndProcessingInstructions_ReturnBadRequest(string xml)
        {
            SoapEndpointOptions options = new SoapEndpointOptions { EnabledVersions = SoapVersionFlags.Soap11 };
            SoapOperationTable table = CreateEchoTable(options);

            SoapProtocolException ex = Assert.Throws<SoapProtocolException>(() =>
                SoapEnvelopeReader.ParseAndPrepare(
                    Encoding.UTF8.GetBytes(xml),
                    SoapVersion.Soap11,
                    "http://tempuri.org/IEchoService/EchoString",
                    table,
                    options,
                    new FakeServiceProvider(),
                    new ClaimsPrincipal()));

            Assert.Equal(HttpStatusCode.BadRequest, ex.HttpStatusCode);
            Assert.Null(ex.SoapVersion);
        }

        [Fact]
        public void ParseAndPrepare_LeadingXmlDeclarationAtOffset0OrAfterBom_Succeeds()
        {
            SoapEndpointOptions options = new SoapEndpointOptions { EnabledVersions = SoapVersionFlags.Soap11 };
            SoapOperationTable table = CreateEchoTable(options);
            byte[] xmlWithDecl = "<?xml version=\"1.0\" encoding=\"utf-8\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>ok</echo></EchoString></s:Body></s:Envelope>"u8.ToArray();
            byte[] xmlWithBomAndDecl = [0xEF, 0xBB, 0xBF, .. xmlWithDecl];

            PreparedSoapCall call1 = SoapEnvelopeReader.ParseAndPrepare(
                xmlWithDecl,
                SoapVersion.Soap11,
                "http://tempuri.org/IEchoService/EchoString",
                table,
                options,
                new FakeServiceProvider(),
                new ClaimsPrincipal());
            Assert.Equal("http://tempuri.org/IEchoService/EchoString", call1.Context.Action);

            PreparedSoapCall call2 = SoapEnvelopeReader.ParseAndPrepare(
                xmlWithBomAndDecl,
                SoapVersion.Soap11,
                "http://tempuri.org/IEchoService/EchoString",
                table,
                options,
                new FakeServiceProvider(),
                new ClaimsPrincipal());
            Assert.Equal("http://tempuri.org/IEchoService/EchoString", call2.Context.Action);
        }

        [Fact]
        public async Task SoapHeaderProcessor_KnownHeaderPrecedingUnknownMandatoryHeader_InvokesZeroHandlers()
        {
            SoapQName authQName = new SoapQName("urn:auth", "ApiKey");
            RecordingHeaderHandler handler = new RecordingHeaderHandler(authQName);
            SoapHeaderProcessor processor = new SoapHeaderProcessor([handler]);

            SoapHeaderValue knownHeader = SoapHeaderValue.CreateCopy(
                authQName,
                null,
                mustUnderstand: false,
                "<h:ApiKey xmlns:h=\"urn:auth\">k</h:ApiKey>"u8);
            SoapHeaderValue unknownMandatory = SoapHeaderValue.CreateCopy(
                new SoapQName("urn:unknown", "RequireAudit"),
                null,
                mustUnderstand: true,
                "<u:RequireAudit xmlns:u=\"urn:unknown\" />"u8);

            SoapCallContext soap11Context = new SoapCallContext
            {
                Action = "http://tempuri.org/IEchoService/EchoString",
                Headers = [knownHeader, unknownMandatory],
                OperationDescriptor = ProtocolTestFixtures.CreateEchoDescriptor(),
                Services = new FakeServiceProvider(),
                User = new ClaimsPrincipal(),
                Version = SoapVersion.Soap11
            };

            SoapProtocolException ex11 = await Assert.ThrowsAsync<SoapProtocolException>(async () =>
                await processor.ProcessAsync(soap11Context, TestContext.Current.CancellationToken));

            Assert.Equal(SoapFaultKind.MustUnderstand, ex11.FaultKind);
            Assert.Equal(0, handler.InvocationCount);

            SoapCallContext soap12Context = new SoapCallContext
            {
                Action = "http://tempuri.org/IEchoService/EchoString",
                Headers = [knownHeader, unknownMandatory],
                OperationDescriptor = ProtocolTestFixtures.CreateEchoDescriptor(),
                Services = new FakeServiceProvider(),
                User = new ClaimsPrincipal(),
                Version = SoapVersion.Soap12
            };

            SoapProtocolException ex12 = await Assert.ThrowsAsync<SoapProtocolException>(async () =>
                await processor.ProcessAsync(soap12Context, TestContext.Current.CancellationToken));

            Assert.Equal(SoapFaultKind.MustUnderstand, ex12.FaultKind);
            Assert.Equal(0, handler.InvocationCount);
        }

        [Fact]
        public async Task SoapHeaderProcessor_ActorAndRoleTargeting_EnforcesMustUnderstandCorrectly()
        {
            SoapQName authQName = new SoapQName("urn:auth", "ApiKey");
            RecordingHeaderHandler handler = new RecordingHeaderHandler(authQName);
            SoapHeaderProcessor processor = new SoapHeaderProcessor([handler]);

            SoapHeaderValue targetedAuth = SoapHeaderValue.CreateCopy(authQName, null, mustUnderstand: true, "<h:ApiKey xmlns:h=\"urn:auth\">k</h:ApiKey>"u8);
            SoapHeaderValue nonTargetedMandatory = SoapHeaderValue.CreateCopy(
                new SoapQName("urn:intermediary", "Route"),
                "urn:other-actor",
                mustUnderstand: true,
                "<r:Route xmlns:r=\"urn:intermediary\" />"u8);
            SoapHeaderValue optionalUnknown = SoapHeaderValue.CreateCopy(
                new SoapQName("urn:trace", "TraceId"),
                SoapConstants.Soap11NextActorUri,
                mustUnderstand: false,
                "<t:TraceId xmlns:t=\"urn:trace\">1</t:TraceId>"u8);

            SoapCallContext soap11Context = new SoapCallContext
            {
                Action = "http://tempuri.org/IEchoService/EchoString",
                Headers = [targetedAuth, nonTargetedMandatory, optionalUnknown],
                OperationDescriptor = ProtocolTestFixtures.CreateEchoDescriptor(),
                Services = new FakeServiceProvider(),
                User = new ClaimsPrincipal(),
                Version = SoapVersion.Soap11
            };

            await processor.ProcessAsync(soap11Context, TestContext.Current.CancellationToken);
            Assert.Equal(1, handler.InvocationCount);

            SoapHeaderValue soap12NoneRoleMandatory = SoapHeaderValue.CreateCopy(
                new SoapQName("urn:none", "IgnoreMe"),
                SoapConstants.Soap12NoneRoleUri,
                mustUnderstand: true,
                "<n:IgnoreMe xmlns:n=\"urn:none\" />"u8);
            SoapHeaderValue soap12UltimateUnknown = SoapHeaderValue.CreateCopy(
                new SoapQName("urn:unknown", "Mandatory"),
                SoapConstants.Soap12UltimateReceiverRoleUri,
                mustUnderstand: true,
                "<u:Mandatory xmlns:u=\"urn:unknown\" />"u8);

            SoapCallContext soap12Context = new SoapCallContext
            {
                Action = "http://tempuri.org/IEchoService/EchoString",
                Headers = [soap12NoneRoleMandatory, soap12UltimateUnknown],
                OperationDescriptor = ProtocolTestFixtures.CreateEchoDescriptor(),
                Services = new FakeServiceProvider(),
                User = new ClaimsPrincipal(),
                Version = SoapVersion.Soap12
            };

            SoapProtocolException mustUnderstandEx = await Assert.ThrowsAsync<SoapProtocolException>(async () =>
                await processor.ProcessAsync(soap12Context, TestContext.Current.CancellationToken));

            Assert.Equal(SoapFaultKind.MustUnderstand, mustUnderstandEx.FaultKind);
            Assert.Equal(HttpStatusCode.InternalServerError, mustUnderstandEx.HttpStatusCode);

            System.Collections.Generic.IReadOnlyList<SoapQName> notUnderstood = processor.GetUnprocessedMandatoryHeaders(
                SoapVersion.Soap12,
                soap12Context.Headers);

            Assert.Single(notUnderstood);
            Assert.Equal(new SoapQName("urn:unknown", "Mandatory"), notUnderstood[0]);
        }

        #endregion

        #region Private Methods

        private static SoapOperationTable CreateEchoTable(SoapEndpointOptions options)
        {
            SoapOperationTableBuilder builder = new SoapOperationTableBuilder();
            builder.MapOperation(
                ProtocolTestFixtures.CreateEchoDescriptor(),
                ProtocolTestFixtures.Context.EchoStringRequestWire,
                ProtocolTestFixtures.Context.EchoStringResponseWire,
                static (req, _, _) => ValueTask.FromResult(new EchoStringResponseWire { EchoStringResult = req.Echo }));

            return builder.Build(options);
        }

        #endregion

    }

}
