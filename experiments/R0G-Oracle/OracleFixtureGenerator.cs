using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

using FastSoap.R0G.Oracle.Contracts;
using FastSoap.R0G.Oracle.Infrastructure;
using FastSoap.R0G.Oracle.Services;

namespace FastSoap.R0G.Oracle
{
    /// <summary>
    /// Metadata sidecar serialized alongside every captured SOAP wire fixture.
    /// </summary>
    public sealed class FixtureMetadata
    {
        #region Properties

        /// <summary>
        /// Gets the CoreWCF test suite class or scenario this fixture maps to.
        /// </summary>
        public required string CoreWcfMapping { get; init; }

        /// <summary>
        /// Gets the expected FastSoap classification (<c>supported</c>, <c>excluded</c>, or <c>pending</c>).
        /// </summary>
        public required string ExpectedOutcome { get; init; }

        /// <summary>
        /// Gets the optional FastSoap rejection note for negative fixtures.
        /// </summary>
        public string? FastSoapNote { get; init; }

        /// <summary>
        /// Gets the canonical fixture identifier.
        /// </summary>
        public required string FixtureId { get; init; }

        /// <summary>
        /// Gets the target HTTP endpoint path.
        /// </summary>
        public required string Path { get; init; }

        /// <summary>
        /// Gets the SHA-256 hex digest of the request file.
        /// </summary>
        public required string RequestSha256 { get; init; }

        /// <summary>
        /// Gets the HTTP <c>Content-Type</c> request header.
        /// </summary>
        public string? RequestContentType { get; init; }

        /// <summary>
        /// Gets the HTTP <c>SOAPAction</c> request header, if applicable.
        /// </summary>
        public string? RequestSoapAction { get; init; }

        /// <summary>
        /// Gets the HTTP <c>Content-Type</c> response header returned by CoreWCF.
        /// </summary>
        public string? ResponseContentType { get; init; }

        /// <summary>
        /// Gets the SHA-256 hex digest of the response file.
        /// </summary>
        public required string ResponseSha256 { get; init; }

        /// <summary>
        /// Gets the HTTP status code returned by CoreWCF.
        /// </summary>
        public required int ResponseStatusCode { get; init; }

        /// <summary>
        /// Gets the serializer used (<c>DataContractSerializer</c>, <c>XmlSerializer</c>, or <c>None</c>).
        /// </summary>
        public required string Serializer { get; init; }

        /// <summary>
        /// Gets the SOAP version (<c>Soap11</c> or <c>Soap12</c>).
        /// </summary>
        public required string SoapVersion { get; init; }

        #endregion
    }

    /// <summary>
    /// Root machine-readable compatibility corpus manifest written to <c>tests/fixtures/manifest.json</c>.
    /// </summary>
    public sealed class CorpusManifest
    {
        #region Properties

        /// <summary>
        /// Gets the list of all captured fixture entries.
        /// </summary>
        public required List<CorpusManifestEntry> Entries { get; init; }

        /// <summary>
        /// Gets the pinned oracle library metadata.
        /// </summary>
        public required Dictionary<string, string> OracleVersions { get; init; }

        /// <summary>
        /// Gets the corpus schema version.
        /// </summary>
        public required string SchemaVersion { get; init; }

        #endregion
    }

    /// <summary>
    /// Single entry in <see cref="CorpusManifest"/>.
    /// </summary>
    public sealed class CorpusManifestEntry
    {
        #region Properties

        /// <summary>
        /// Gets the fixture category (<c>positive</c>, <c>serializer</c>, <c>wsdl</c>, or <c>negative</c>).
        /// </summary>
        public required string Category { get; init; }

        /// <summary>
        /// Gets the CoreWCF test suite mapping.
        /// </summary>
        public required string CoreWcfMapping { get; init; }

        /// <summary>
        /// Gets the expected FastSoap outcome (<c>supported</c>, <c>excluded</c>, or <c>pending</c>).
        /// </summary>
        public required string ExpectedOutcome { get; init; }

        /// <summary>
        /// Gets the canonical fixture identifier.
        /// </summary>
        public required string FixtureId { get; init; }

        /// <summary>
        /// Gets the primary fixture relative paths under <c>tests/fixtures/</c>.
        /// </summary>
        public required List<string> Files { get; init; }

        /// <summary>
        /// Gets the serializer name.
        /// </summary>
        public required string Serializer { get; init; }

        /// <summary>
        /// Gets the SHA-256 digests keyed by relative file path.
        /// </summary>
        public required Dictionary<string, string> Sha256ByFile { get; init; }

        /// <summary>
        /// Gets the SOAP or metadata profile name.
        /// </summary>
        public required string WireProfile { get; init; }

        #endregion
    }

    /// <summary>
    /// Summary returned after running the CoreWCF + WCF oracle capture pass.
    /// </summary>
    public sealed class OracleRunResult
    {
        #region Properties

        /// <summary>
        /// Gets all captured manifest entries.
        /// </summary>
        public required IReadOnlyList<CorpusManifestEntry> Entries { get; init; }

        /// <summary>
        /// Gets the absolute path to <c>tests/fixtures/</c>.
        /// </summary>
        public required string FixturesDirectory { get; init; }

        /// <summary>
        /// Gets the observed CoreWCF negative responses keyed by fixture ID.
        /// </summary>
        public required IReadOnlyDictionary<string, CapturedWireExchange> NegativeCaptures { get; init; }

        /// <summary>
        /// Gets the observed CoreWCF + WCF positive responses keyed by fixture ID.
        /// </summary>
        public required IReadOnlyDictionary<string, CapturedWireExchange> PositiveCaptures { get; init; }

        /// <summary>
        /// Gets the captured WSDL/XSD payloads keyed by relative file path.
        /// </summary>
        public required IReadOnlyDictionary<string, byte[]> WsdlArtifacts { get; init; }

        #endregion
    }

    /// <summary>
    /// Captures and writes golden SOAP 1.1/1.2, BCL serializer, WSDL/XSD, and negative XML fixtures
    /// from live CoreWCF 1.9.2 and WCF <c>System.ServiceModel.Http</c>.
    /// </summary>
    public static class OracleFixtureGenerator
    {
        #region Fields

        private static readonly JsonSerializerOptions s_jsonOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        private static readonly Encoding s_utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private static readonly Encoding s_utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

        #endregion

        #region Public Methods

        /// <summary>
        /// Executes the live CoreWCF 1.9.2 + WCF client oracle capture and writes/updates all fixtures under
        /// <paramref name="fixturesDirectory"/> when <paramref name="writeToDisk"/> is <see langword="true"/>.
        /// </summary>
        /// <param name="fixturesDirectory">Target directory (<c>tests/fixtures</c>).</param>
        /// <param name="writeToDisk">Whether to persist generated fixture files to disk.</param>
        /// <returns>The complete <see cref="OracleRunResult"/>.</returns>
        public static async Task<OracleRunResult> RunAsync(string fixturesDirectory, bool writeToDisk)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fixturesDirectory);

            List<CorpusManifestEntry> entries = [];
            Dictionary<string, CapturedWireExchange> positiveCaptures = new Dictionary<string, CapturedWireExchange>(StringComparer.Ordinal);
            Dictionary<string, CapturedWireExchange> negativeCaptures = new Dictionary<string, CapturedWireExchange>(StringComparer.Ordinal);
            Dictionary<string, byte[]> wsdlArtifacts = new Dictionary<string, byte[]>(StringComparer.Ordinal);

            await using (WireCaptureHost host = await WireCaptureHost.StartOracleAsync().ConfigureAwait(false))
            {
                await CapturePositiveScenariosAsync(host, fixturesDirectory, writeToDisk, entries, positiveCaptures).ConfigureAwait(false);
                await CaptureNegativeScenariosAsync(host, fixturesDirectory, writeToDisk, entries, negativeCaptures).ConfigureAwait(false);
            }

            CaptureSerializerPayloads(fixturesDirectory, writeToDisk, entries);
            await CaptureWsdlArtifactsAsync(fixturesDirectory, writeToDisk, entries, wsdlArtifacts).ConfigureAwait(false);

            if (writeToDisk)
            {
                CorpusManifest manifest = new CorpusManifest
                {
                    SchemaVersion = "1.0.0",
                    OracleVersions = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["CoreWCF.Http"] = typeof(CoreWCF.BasicHttpBinding).Assembly.GetName().Version?.ToString() ?? "1.9.2",
                        ["CoreWCF.Primitives"] = typeof(CoreWCF.OperationContractAttribute).Assembly.GetName().Version?.ToString() ?? "1.9.2",
                        ["System.ServiceModel.Http"] = typeof(BasicHttpBinding).Assembly.GetName().Version?.ToString() ?? "10.0.652802",
                        ["Runtime"] = Environment.Version.ToString()
                    },
                    Entries = entries
                };

                string manifestPath = Path.Combine(fixturesDirectory, "manifest.json");
                string manifestJson = JsonSerializer.Serialize(manifest, s_jsonOptions) + "\n";
                await File.WriteAllTextAsync(manifestPath, manifestJson, s_utf8NoBom).ConfigureAwait(false);
            }

            return new OracleRunResult
            {
                Entries = entries,
                FixturesDirectory = fixturesDirectory,
                NegativeCaptures = negativeCaptures,
                PositiveCaptures = positiveCaptures,
                WsdlArtifacts = wsdlArtifacts
            };
        }

        /// <summary>
        /// Computes lowercase SHA-256 hex digest for a byte array.
        /// </summary>
        /// <param name="bytes">Input bytes.</param>
        /// <returns>Lowercase hex SHA-256 digest.</returns>
        public static string ComputeSha256Hex(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);

            byte[] hash = SHA256.HashData(bytes);

            return Convert.ToHexStringLower(hash);
        }

        /// <summary>
        /// Resolves the repository root directory by walking upward for <c>FastSoap.slnx</c>.
        /// </summary>
        /// <returns>The repository root directory path.</returns>
        public static string FindRepositoryRoot()
        {
            DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current is not null)
            {
                string solutionPath = Path.Combine(current.FullName, "FastSoap.slnx");
                if (File.Exists(solutionPath))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root containing FastSoap.slnx.");
        }

        #endregion

        #region Private Methods

        private static async Task CaptureNegativeScenariosAsync(
            WireCaptureHost host,
            string fixturesDirectory,
            bool writeToDisk,
            List<CorpusManifestEntry> entries,
            Dictionary<string, CapturedWireExchange> negativeCaptures)
        {
            using HttpClient httpClient = new HttpClient();
            IReadOnlyList<AdversarialTestCase> cases = AdversarialCases.GetAll();

            foreach (AdversarialTestCase testCase in cases)
            {
                host.ClearCaptures();

                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, host.CreateEndpointUri(testCase.Path));
                ByteArrayContent content = new ByteArrayContent(testCase.RequestBody);
                content.Headers.ContentType = MediaTypeHeaderValue.Parse(testCase.RequestContentType);
                content.Headers.ContentLength = testCase.RequestBody.Length;
                request.Content = content;

                if (testCase.RequestSoapAction is not null)
                {
                    request.Headers.TryAddWithoutValidation("SOAPAction", testCase.RequestSoapAction);
                }

                using HttpResponseMessage response = await httpClient.SendAsync(request).ConfigureAwait(false);
                byte[] responseBytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                string? responseContentType = response.Content.Headers.ContentType?.ToString();

                CapturedWireExchange capture = new CapturedWireExchange
                {
                    Path = testCase.Path,
                    RequestBody = testCase.RequestBody,
                    RequestContentType = testCase.RequestContentType,
                    RequestSoapAction = testCase.RequestSoapAction,
                    ResponseBody = responseBytes,
                    ResponseContentType = responseContentType,
                    ResponseStatusCode = (int)response.StatusCode
                };

                negativeCaptures.Add(testCase.FixtureId, capture);

                string requestExt = testCase.IsBinaryRequest ? ".request.bin" : ".request.xml";
                string relBase = "negative/" + testCase.FileBaseName;
                string relReq = relBase + requestExt;
                string relResp = relBase + ".corewcf-response.bin";
                string relMeta = relBase + ".meta.json";

                FixtureMetadata meta = new FixtureMetadata
                {
                    FixtureId = testCase.FixtureId,
                    SoapVersion = testCase.SoapVersion,
                    Serializer = "None",
                    Path = testCase.Path,
                    RequestContentType = testCase.RequestContentType,
                    RequestSoapAction = testCase.RequestSoapAction,
                    ResponseStatusCode = capture.ResponseStatusCode,
                    ResponseContentType = capture.ResponseContentType,
                    RequestSha256 = ComputeSha256Hex(capture.RequestBody),
                    ResponseSha256 = ComputeSha256Hex(capture.ResponseBody),
                    CoreWcfMapping = testCase.MappedCoreWcfTest,
                    ExpectedOutcome = "excluded",
                    FastSoapNote = testCase.ExpectedFastSoapOutcome
                };

                if (writeToDisk)
                {
                    WriteBytes(Path.Combine(fixturesDirectory, relReq), capture.RequestBody);
                    WriteBytes(Path.Combine(fixturesDirectory, relResp), capture.ResponseBody);
                    WriteMetadata(Path.Combine(fixturesDirectory, relMeta), meta);
                }

                entries.Add(new CorpusManifestEntry
                {
                    FixtureId = testCase.FixtureId,
                    Category = "negative",
                    WireProfile = testCase.SoapVersion,
                    Serializer = "None",
                    ExpectedOutcome = "excluded",
                    CoreWcfMapping = testCase.MappedCoreWcfTest,
                    Files = [relReq, relResp, relMeta],
                    Sha256ByFile = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [relReq] = meta.RequestSha256,
                        [relResp] = meta.ResponseSha256
                    }
                });
            }
        }

        private static async Task CapturePositiveScenariosAsync(
            WireCaptureHost host,
            string fixturesDirectory,
            bool writeToDisk,
            List<CorpusManifestEntry> entries,
            Dictionary<string, CapturedWireExchange> positiveCaptures)
        {
            TransferRequest standardTransfer = new TransferRequest
            {
                AccountNumber = "123456",
                Amount = 100.50m,
                Currency = "USD",
                Metadata = new TransferMetadata
                {
                    CorrelationId = "CORR-2026-0001",
                    Reference = "INV-42"
                },
                Note = "Invoice settlement"
            };

            TransferRequest nilTransfer = new TransferRequest
            {
                AccountNumber = "123456",
                Amount = 25.00m,
                Currency = "EUR",
                Metadata = null,
                Note = null
            };

            // SOAP 1.1 Echo (DCS)
            await CaptureWcfCallAsync<IEchoDcsClient>(
                host,
                soap12: false,
                "/soap11/echo-dcs.svc",
                "SOAP11-ECHO-DCS",
                "Soap11",
                "DataContractSerializer",
                "supported",
                "CoreWCF.Http.Tests.SimpleTest.BasicHttpRequestReplyEchoString",
                "soap11/echo/echo-dcs",
                fixturesDirectory,
                writeToDisk,
                entries,
                positiveCaptures,
                async client => _ = await client.EchoStringAsync("hello").ConfigureAwait(false)).ConfigureAwait(false);

            // SOAP 1.1 Echo Non-ASCII UTF-8 (DCS)
            await CaptureWcfCallAsync<IEchoDcsClient>(
                host,
                soap12: false,
                "/soap11/echo-dcs.svc",
                "SOAP11-ECHO-DCS-NONASCII",
                "Soap11",
                "DataContractSerializer",
                "supported",
                "CoreWCF.Http.Tests.SimpleTest + FastSoap Plan §12.1 (non-ASCII UTF-8)",
                "soap11/echo/echo-dcs-nonascii",
                fixturesDirectory,
                writeToDisk,
                entries,
                positiveCaptures,
                async client => _ = await client.EchoStringAsync("Hello, 世界! € — café 🚀").ConfigureAwait(false)).ConfigureAwait(false);

            // SOAP 1.1 Echo Null (DCS)
            await CaptureWcfCallAsync<IEchoDcsClient>(
                host,
                soap12: false,
                "/soap11/echo-dcs.svc",
                "SOAP11-ECHO-DCS-NULL",
                "Soap11",
                "DataContractSerializer",
                "supported",
                "CoreWCF.Http.Tests.SimpleTest + FastSoap Plan §12.1 (nil vs omission)",
                "soap11/echo/echo-dcs-null",
                fixturesDirectory,
                writeToDisk,
                entries,
                positiveCaptures,
                async client => _ = await client.EchoStringAsync(null).ConfigureAwait(false)).ConfigureAwait(false);

            // SOAP 1.1 Echo (XmlSerializer)
            await CaptureWcfCallAsync<IEchoXmlSerializerClient>(
                host,
                soap12: false,
                "/soap11/echo-xmlser.svc",
                "SOAP11-ECHO-XMLSER",
                "Soap11",
                "XmlSerializer",
                "supported",
                "CoreWCF.Http.Tests.ServiceWithCoreWCFXmlSerializerFormatTest",
                "soap11/echo/echo-xmlser",
                fixturesDirectory,
                writeToDisk,
                entries,
                positiveCaptures,
                async client => _ = await client.EchoStringAsync("hello").ConfigureAwait(false)).ConfigureAwait(false);

            // SOAP 1.1 Echo with SOAP Header & Body MessageContract
            await CaptureWcfCallAsync<IEchoWithHeadersClient>(
                host,
                soap12: false,
                "/soap11/echo-headers.svc",
                "SOAP11-ECHO-HEADER",
                "Soap11",
                "XmlSerializer",
                "supported",
                "CoreWCF.Http.Tests.ServiceWithMessageBodyAndHeaderTest.BasicScenarioServiceMessageBody",
                "soap11/headers/echo-header-optional",
                fixturesDirectory,
                writeToDisk,
                entries,
                positiveCaptures,
                async client => _ = await client.EchoWithMessageContractAsync(new EchoHeaderClientRequestMessage
                {
                    ApiKey = "DEVKEYTOTEST",
                    Text = "Message Hello"
                }).ConfigureAwait(false)).ConfigureAwait(false);

            // SOAP 1.1 Transfer (Default CLR DataContract Namespace)
            await CaptureWcfCallAsync<IPaymentDcsDefaultClient>(
                host,
                soap12: false,
                "/soap11/payment-dcs-default.svc",
                "SOAP11-TRANSFER-DCS-DEFAULT",
                "Soap11",
                "DataContractSerializer",
                "pending",
                "CoreWCF.Http.Tests.ComplexSerializationTests.DataContractNameSpaceComplexSerialization",
                "soap11/transfer/transfer-dcs-default",
                fixturesDirectory,
                writeToDisk,
                entries,
                positiveCaptures,
                async client => _ = await client.TransferAsync(new TransferRequestDefaultDcs
                {
                    AccountNumber = "123456",
                    Amount = 100.50m,
                    Currency = "USD",
                    Note = null
                }).ConfigureAwait(false)).ConfigureAwait(false);

            // SOAP 1.1 Transfer (Explicit urn:payments DataContract Namespace)
            await CaptureWcfCallAsync<IPaymentDcsUrnClient>(
                host,
                soap12: false,
                "/soap11/payment-dcs-urn.svc",
                "SOAP11-TRANSFER-DCS-URN",
                "Soap11",
                "DataContractSerializer",
                "supported",
                "CoreWCF.Http.Tests.ServiceContractNameSpaceTests + FastSoap Plan §3.1/§3.2",
                "soap11/transfer/transfer-dcs-urnpayments",
                fixturesDirectory,
                writeToDisk,
                entries,
                positiveCaptures,
                async client => _ = await client.TransferAsync(standardTransfer).ConfigureAwait(false)).ConfigureAwait(false);

            // SOAP 1.1 Transfer Nilable Fields (Explicit urn:payments DCS)
            await CaptureWcfCallAsync<IPaymentDcsUrnClient>(
                host,
                soap12: false,
                "/soap11/payment-dcs-urn.svc",
                "SOAP11-TRANSFER-DCS-NIL",
                "Soap11",
                "DataContractSerializer",
                "supported",
                "CoreWCF.DataContractSerialization.TestCorpus.SanityNullable + FastSoap Plan §12.1",
                "soap11/transfer/transfer-dcs-urnpayments-nil",
                fixturesDirectory,
                writeToDisk,
                entries,
                positiveCaptures,
                async client => _ = await client.TransferAsync(nilTransfer).ConfigureAwait(false)).ConfigureAwait(false);

            // SOAP 1.1 Transfer (XmlSerializer urn:payments)
            await CaptureWcfCallAsync<IPaymentXmlSerializerClient>(
                host,
                soap12: false,
                "/soap11/payment-xmlser.svc",
                "SOAP11-TRANSFER-XMLSER",
                "Soap11",
                "XmlSerializer",
                "supported",
                "CoreWCF.Http.Tests.ServiceWithCoreWCFXmlSerializerFormatTest + FastSoap Plan §3.2",
                "soap11/transfer/transfer-xmlser-urnpayments",
                fixturesDirectory,
                writeToDisk,
                entries,
                positiveCaptures,
                async client => _ = await client.TransferAsync(standardTransfer).ConfigureAwait(false)).ConfigureAwait(false);

            // SOAP 1.1 BatchTransfer Collection (DCS)
            await CaptureWcfCallAsync<IPaymentDcsUrnClient>(
                host,
                soap12: false,
                "/soap11/payment-dcs-urn.svc",
                "SOAP11-BATCH-TRANSFER-DCS",
                "Soap11",
                "DataContractSerializer",
                "supported",
                "CoreWCF.Http.Tests.TaskCollectionsTests + CoreWCF.DataContractSerialization.TestCorpus.SanityCollections",
                "soap11/collections/batch-transfer-dcs",
                fixturesDirectory,
                writeToDisk,
                entries,
                positiveCaptures,
                async client => _ = await client.BatchTransferAsync(new BatchTransferRequest
                {
                    BatchId = "BATCH-01",
                    Items = [standardTransfer, nilTransfer]
                }).ConfigureAwait(false)).ConfigureAwait(false);

            // SOAP 1.1 Typed FaultException<PaymentFaultDetail>
            await CaptureWcfCallAsync<IPaymentDcsUrnClient>(
                host,
                soap12: false,
                "/soap11/payment-dcs-urn.svc",
                "SOAP11-TRANSFER-FAULT-TYPED",
                "Soap11",
                "DataContractSerializer",
                "supported",
                "CoreWCF.Http.Tests.FaultContractTests.DatacontractFaults",
                "soap11/faults/transfer-typed-fault",
                fixturesDirectory,
                writeToDisk,
                entries,
                positiveCaptures,
                async client =>
                {
                    try
                    {
                        _ = await client.TransferAsync(new TransferRequest
                        {
                            AccountNumber = "123456",
                            Amount = -5.00m,
                            Currency = "USD"
                        }).ConfigureAwait(false);
                    }
                    catch (FaultException<PaymentFaultDetail>)
                    {
                    }
                }).ConfigureAwait(false);

            // SOAP 1.1 Unhandled Server Receiver Fault (IncludeExceptionDetailInFaults = false)
            await CaptureWcfCallAsync<IPaymentDcsUrnClient>(
                host,
                soap12: false,
                "/soap11/payment-dcs-urn.svc",
                "SOAP11-TRANSFER-FAULT-RECEIVER",
                "Soap11",
                "DataContractSerializer",
                "supported",
                "CoreWCF.Primitives.Tests.BasicFaultTest + FastSoap Plan §6.4",
                "soap11/faults/transfer-receiver-fault",
                fixturesDirectory,
                writeToDisk,
                entries,
                positiveCaptures,
                async client =>
                {
                    try
                    {
                        _ = await client.TransferAsync(new TransferRequest
                        {
                            AccountNumber = "FAIL-INTERNAL",
                            Amount = 10.00m,
                            Currency = "USD"
                        }).ConfigureAwait(false);
                    }
                    catch (FaultException)
                    {
                    }
                }).ConfigureAwait(false);

            // SOAP 1.2 Echo (DCS, without WS-Addressing)
            await CaptureWcfCallAsync<IEchoDcsClient>(
                host,
                soap12: true,
                "/soap12/echo-dcs.svc",
                "SOAP12-ECHO-DCS",
                "Soap12",
                "DataContractSerializer",
                "supported",
                "CoreWCF.Http.Tests.MessageVersionsTests.EchoStringTest (MessageVersion.Soap12)",
                "soap12/echo/echo-dcs",
                fixturesDirectory,
                writeToDisk,
                entries,
                positiveCaptures,
                async client => _ = await client.EchoStringAsync("hello").ConfigureAwait(false)).ConfigureAwait(false);

            // SOAP 1.2 Echo (XmlSerializer, without WS-Addressing)
            await CaptureWcfCallAsync<IEchoXmlSerializerClient>(
                host,
                soap12: true,
                "/soap12/echo-xmlser.svc",
                "SOAP12-ECHO-XMLSER",
                "Soap12",
                "XmlSerializer",
                "supported",
                "CoreWCF.Http.Tests.MessageVersionsTests + ServiceWithCoreWCFXmlSerializerFormatTest",
                "soap12/echo/echo-xmlser",
                fixturesDirectory,
                writeToDisk,
                entries,
                positiveCaptures,
                async client => _ = await client.EchoStringAsync("hello").ConfigureAwait(false)).ConfigureAwait(false);

            // SOAP 1.2 Transfer (Explicit urn:payments DCS, without WS-Addressing)
            await CaptureWcfCallAsync<IPaymentDcsUrnClient>(
                host,
                soap12: true,
                "/soap12/payment-dcs-urn.svc",
                "SOAP12-TRANSFER-DCS-URN",
                "Soap12",
                "DataContractSerializer",
                "supported",
                "CoreWCF.Http.Tests.MessageVersionsTests (Soap12) + FastSoap Plan §3.2 & §6.1",
                "soap12/transfer/transfer-dcs-urnpayments",
                fixturesDirectory,
                writeToDisk,
                entries,
                positiveCaptures,
                async client => _ = await client.TransferAsync(standardTransfer).ConfigureAwait(false)).ConfigureAwait(false);

            // SOAP 1.2 Transfer (XmlSerializer urn:payments, without WS-Addressing)
            await CaptureWcfCallAsync<IPaymentXmlSerializerClient>(
                host,
                soap12: true,
                "/soap12/payment-xmlser.svc",
                "SOAP12-TRANSFER-XMLSER",
                "Soap12",
                "XmlSerializer",
                "supported",
                "CoreWCF.Http.Tests.MessageVersionsTests (Soap12) + ServiceWithCoreWCFXmlSerializerFormatTest",
                "soap12/transfer/transfer-xmlser-urnpayments",
                fixturesDirectory,
                writeToDisk,
                entries,
                positiveCaptures,
                async client => _ = await client.TransferAsync(standardTransfer).ConfigureAwait(false)).ConfigureAwait(false);

            // SOAP 1.2 Typed FaultException<PaymentFaultDetail>
            await CaptureWcfCallAsync<IPaymentDcsUrnClient>(
                host,
                soap12: true,
                "/soap12/payment-dcs-urn.svc",
                "SOAP12-TRANSFER-FAULT-TYPED",
                "Soap12",
                "DataContractSerializer",
                "supported",
                "CoreWCF.Primitives.Tests.BasicFaultTest + CoreWCF.Http.Tests.FaultContractTests",
                "soap12/faults/transfer-typed-fault",
                fixturesDirectory,
                writeToDisk,
                entries,
                positiveCaptures,
                async client =>
                {
                    try
                    {
                        _ = await client.TransferAsync(new TransferRequest
                        {
                            AccountNumber = "123456",
                            Amount = -5.00m,
                            Currency = "USD"
                        }).ConfigureAwait(false);
                    }
                    catch (FaultException<PaymentFaultDetail>)
                    {
                    }
                }).ConfigureAwait(false);

            // SOAP 1.1 Valid UTF-8 BOM Prefix (must succeed with HTTP 200)
            byte[] validBomBody =
            [
                .. s_utf8WithBom.GetPreamble(),
                .. s_utf8NoBom.GetBytes(
                    "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                    "<s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>hello</echo></EchoString></s:Body>" +
                    "</s:Envelope>")
            ];
            await CaptureRawPositiveSoapCallAsync(
                host,
                "/soap11/echo-dcs.svc",
                "SOAP11-ECHO-DCS-UTF8BOM",
                "Soap11",
                "DataContractSerializer",
                "supported",
                "CoreWCF TextMessageEncoder UTF-8 BOM handling + FastSoap Plan §6.1 & ADR-001 §3.3 BLOCKER-2",
                "soap11/echo/echo-dcs-utf8bom",
                "text/xml; charset=utf-8",
                $"\"{EchoConstants.EchoStringAction}\"",
                validBomBody,
                expectedCoreWcfStatusCode: 200,
                useCoreWcfResponseExtension: false,
                "Valid UTF-8 BOM prefix (EF BB BF) must succeed with HTTP 200",
                fixturesDirectory,
                writeToDisk,
                entries,
                positiveCaptures).ConfigureAwait(false);

            // SOAP 1.1 mustUnderstand='1' targeted at a non-matching actor (must NOT fault at ultimate receiver)
            byte[] nonMatchingActorBody = s_utf8NoBom.GetBytes(
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                "<s:Header><x:IntermediaryHeader xmlns:x=\"urn:other-node\" s:actor=\"http://example.org/intermediary\" s:mustUnderstand=\"1\">value</x:IntermediaryHeader></s:Header>" +
                "<s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>hello</echo></EchoString></s:Body>" +
                "</s:Envelope>");
            await CaptureRawPositiveSoapCallAsync(
                host,
                "/soap11/echo-dcs.svc",
                "SOAP11-HEADER-MUSTUNDERSTAND-OTHER-ACTOR",
                "Soap11",
                "DataContractSerializer",
                "supported",
                "SOAP 1.1 §4.2.2/§4.2.3 non-matching actor with mustUnderstand='1' + FastSoap Plan §6.3 & ADR-001 §3.3 BLOCKER-2",
                "soap11/headers/echo-mustunderstand-other-actor",
                "text/xml; charset=utf-8",
                $"\"{EchoConstants.EchoStringAction}\"",
                nonMatchingActorBody,
                expectedCoreWcfStatusCode: 500,
                useCoreWcfResponseExtension: true,
                "SOAP 1.1 mustUnderstand='1' targeted at a non-matching actor must NOT fault at ultimate receiver (FastSoap expected HTTP 200 EchoStringResponse; CoreWCF 1.9.2 default ValidateMustUnderstand=true returns HTTP 500)",
                fixturesDirectory,
                writeToDisk,
                entries,
                positiveCaptures).ConfigureAwait(false);

            // SOAP 1.2 mustUnderstand='true' targeted at role/none (must NOT fault)
            byte[] soap12RoleNoneBody = s_utf8NoBom.GetBytes(
                "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\">" +
                "<s:Header><x:NoneHeader xmlns:x=\"urn:other-node\" s:role=\"http://www.w3.org/2003/05/soap-envelope/role/none\" s:mustUnderstand=\"true\">value</x:NoneHeader></s:Header>" +
                "<s:Body><EchoString xmlns=\"http://tempuri.org/\"><echo>hello</echo></EchoString></s:Body>" +
                "</s:Envelope>");
            await CaptureRawPositiveSoapCallAsync(
                host,
                "/soap12/echo-dcs.svc",
                "SOAP12-HEADER-MUSTUNDERSTAND-ROLE-NONE",
                "Soap12",
                "DataContractSerializer",
                "supported",
                "SOAP 1.2 Part 1 §2.2/§2.4 role/none with mustUnderstand='true' + FastSoap Plan §6.3 & ADR-001 §3.3 BLOCKER-2",
                "soap12/headers/echo-mustunderstand-role-none",
                $"application/soap+xml; charset=utf-8; action=\"{EchoConstants.EchoStringAction}\"",
                null,
                soap12RoleNoneBody,
                expectedCoreWcfStatusCode: 500,
                useCoreWcfResponseExtension: true,
                "SOAP 1.2 mustUnderstand='true' targeted at role/none must NOT fault (FastSoap expected HTTP 200 EchoStringResponse; CoreWCF 1.9.2 default ValidateMustUnderstand=true returns HTTP 500)",
                fixturesDirectory,
                writeToDisk,
                entries,
                positiveCaptures).ConfigureAwait(false);
        }

        private static async Task CaptureRawPositiveSoapCallAsync(
            WireCaptureHost host,
            string path,
            string fixtureId,
            string soapVersion,
            string serializer,
            string expectedOutcome,
            string coreWcfMapping,
            string relativeBase,
            string requestContentType,
            string? requestSoapAction,
            byte[] requestBody,
            int expectedCoreWcfStatusCode,
            bool useCoreWcfResponseExtension,
            string fastSoapNote,
            string fixturesDirectory,
            bool writeToDisk,
            List<CorpusManifestEntry> entries,
            Dictionary<string, CapturedWireExchange> positiveCaptures)
        {
            host.ClearCaptures();

            using HttpClient httpClient = new HttpClient();
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, host.CreateEndpointUri(path));
            ByteArrayContent content = new ByteArrayContent(requestBody);
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(requestContentType);
            content.Headers.ContentLength = requestBody.Length;
            request.Content = content;

            if (requestSoapAction is not null)
            {
                request.Headers.TryAddWithoutValidation("SOAPAction", requestSoapAction);
            }

            using HttpResponseMessage response = await httpClient.SendAsync(request).ConfigureAwait(false);
            _ = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

            CapturedWireExchange capture = host.DequeueSingleCapture();
            if (capture.ResponseStatusCode != expectedCoreWcfStatusCode)
            {
                throw new InvalidOperationException(
                    $"Expected CoreWCF HTTP {expectedCoreWcfStatusCode} for protocol fixture '{fixtureId}', but observed {capture.ResponseStatusCode}.");
            }

            positiveCaptures.Add(fixtureId, capture);

            string relReq = relativeBase + ".request.xml";
            string relResp = relativeBase + (useCoreWcfResponseExtension ? ".corewcf-response.bin" : ".response.xml");
            string relMeta = relativeBase + ".meta.json";

            FixtureMetadata meta = new FixtureMetadata
            {
                FixtureId = fixtureId,
                SoapVersion = soapVersion,
                Serializer = serializer,
                Path = path,
                RequestContentType = capture.RequestContentType,
                RequestSoapAction = capture.RequestSoapAction,
                ResponseStatusCode = capture.ResponseStatusCode,
                ResponseContentType = capture.ResponseContentType,
                RequestSha256 = ComputeSha256Hex(capture.RequestBody),
                ResponseSha256 = ComputeSha256Hex(capture.ResponseBody),
                CoreWcfMapping = coreWcfMapping,
                ExpectedOutcome = expectedOutcome,
                FastSoapNote = fastSoapNote
            };

            if (writeToDisk)
            {
                WriteBytes(Path.Combine(fixturesDirectory, relReq), capture.RequestBody);
                WriteBytes(Path.Combine(fixturesDirectory, relResp), capture.ResponseBody);
                WriteMetadata(Path.Combine(fixturesDirectory, relMeta), meta);
            }

            entries.Add(new CorpusManifestEntry
            {
                FixtureId = fixtureId,
                Category = "positive",
                WireProfile = soapVersion,
                Serializer = serializer,
                ExpectedOutcome = expectedOutcome,
                CoreWcfMapping = coreWcfMapping,
                Files = [relReq, relResp, relMeta],
                Sha256ByFile = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [relReq] = meta.RequestSha256,
                    [relResp] = meta.ResponseSha256
                }
            });
        }

        private static void CaptureSerializerPayloads(
            string fixturesDirectory,
            bool writeToDisk,
            List<CorpusManifestEntry> entries)
        {
            TransferRequest standardTransfer = new TransferRequest
            {
                AccountNumber = "123456",
                Amount = 100.50m,
                Currency = "USD",
                Metadata = new TransferMetadata
                {
                    CorrelationId = "CORR-2026-0001",
                    Reference = "INV-42"
                },
                Note = "Invoice settlement"
            };

            TransferRequest nilTransfer = new TransferRequest
            {
                AccountNumber = "123456",
                Amount = 25.00m,
                Currency = "EUR",
                Metadata = null,
                Note = null
            };

            TransferResult standardResult = new TransferResult
            {
                ProcessedAtUtc = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc),
                Status = PaymentStatus.Completed,
                Successful = true,
                TransactionId = "TX-001"
            };

            TransferRequestDefaultDcs defaultTransfer = new TransferRequestDefaultDcs
            {
                AccountNumber = "123456",
                Amount = 100.50m,
                Currency = "USD",
                Note = null
            };

            BatchTransferRequest batchTransfer = new BatchTransferRequest
            {
                BatchId = "BATCH-01",
                Items = [standardTransfer, nilTransfer]
            };

            PaymentFaultDetail faultDetail = new PaymentFaultDetail
            {
                ErrorCode = "INVALID_AMOUNT",
                Message = "Transfer amount must be non-negative."
            };

            WriteDcsFixture("SER-DCS-ECHO", "serializers/dcs/echo-string.xml", typeof(string), "hello",
                "CoreWCF.DataContractSerialization.TestCorpus.SanityPrimitives + CoreWCF.Primitives.Tests.SerializationTests",
                fixturesDirectory, writeToDisk, entries);
            WriteDcsFixture("SER-DCS-TRANSFER-DEFAULT", "serializers/dcs/transfer-request-default.xml", typeof(TransferRequestDefaultDcs), defaultTransfer,
                "CoreWCF.Http.Tests.ComplexSerializationTests.DataContractNameSpaceComplexSerialization",
                fixturesDirectory, writeToDisk, entries);
            WriteDcsFixture("SER-DCS-TRANSFER-URN", "serializers/dcs/transfer-request-urnpayments.xml", typeof(TransferRequest), standardTransfer,
                "CoreWCF.DataContractSerialization.TestCorpus.SanityCustomNaming + SanityMemberAttributes",
                fixturesDirectory, writeToDisk, entries);
            WriteDcsFixture("SER-DCS-TRANSFER-NIL", "serializers/dcs/transfer-request-urnpayments-nil.xml", typeof(TransferRequest), nilTransfer,
                "CoreWCF.DataContractSerialization.TestCorpus.SanityNullable",
                fixturesDirectory, writeToDisk, entries);
            WriteDcsFixture("SER-DCS-TRANSFER-RESULT", "serializers/dcs/transfer-result-urnpayments.xml", typeof(TransferResult), standardResult,
                "CoreWCF.DataContractSerialization.TestCorpus.SanityEnums + SanityDateAndTimeOnly",
                fixturesDirectory, writeToDisk, entries);
            WriteDcsFixture("SER-DCS-BATCH-TRANSFER", "serializers/dcs/batch-transfer-request.xml", typeof(BatchTransferRequest), batchTransfer,
                "CoreWCF.DataContractSerialization.TestCorpus.SanityCollections",
                fixturesDirectory, writeToDisk, entries);
            WriteDcsFixture("SER-DCS-PAYMENT-FAULT", "serializers/dcs/payment-fault-detail.xml", typeof(PaymentFaultDetail), faultDetail,
                "CoreWCF.Http.Tests.FaultContractTests.DatacontractFaults",
                fixturesDirectory, writeToDisk, entries);

            WriteXmlSerializerFixture("SER-XML-ECHO", "serializers/xmlserializer/echo-string.xml", typeof(string), "hello",
                "CoreWCF.Http.Tests.ServiceWithCoreWCFXmlSerializerFormatTest",
                fixturesDirectory, writeToDisk, entries);
            WriteXmlSerializerFixture("SER-XML-TRANSFER-URN", "serializers/xmlserializer/transfer-request-urnpayments.xml", typeof(TransferRequest), standardTransfer,
                "CoreWCF.Http.Tests.ServiceWithCoreWCFXmlSerializerFormatTest + FastSoap Plan §3.2",
                fixturesDirectory, writeToDisk, entries);
            WriteXmlSerializerFixture("SER-XML-TRANSFER-NIL", "serializers/xmlserializer/transfer-request-urnpayments-nil.xml", typeof(TransferRequest), nilTransfer,
                "CoreWCF.Http.Tests.ServiceWithCoreWCFXmlSerializerFormatTest (IsNullable=true)",
                fixturesDirectory, writeToDisk, entries);
            WriteXmlSerializerFixture("SER-XML-TRANSFER-RESULT", "serializers/xmlserializer/transfer-result-urnpayments.xml", typeof(TransferResult), standardResult,
                "CoreWCF.Http.Tests.ServiceWithCoreWCFXmlSerializerFormatTest + FastSoap Plan §3.2",
                fixturesDirectory, writeToDisk, entries);
            WriteXmlSerializerFixture("SER-XML-BATCH-TRANSFER", "serializers/xmlserializer/batch-transfer-request.xml", typeof(BatchTransferRequest), batchTransfer,
                "CoreWCF.Http.Tests.ServiceWithCoreWCFXmlSerializerFormatTest (collections)",
                fixturesDirectory, writeToDisk, entries);
        }

        private static async Task CaptureWcfCallAsync<TClient>(
            WireCaptureHost host,
            bool soap12,
            string path,
            string fixtureId,
            string soapVersion,
            string serializer,
            string expectedOutcome,
            string coreWcfMapping,
            string relativeBase,
            string fixturesDirectory,
            bool writeToDisk,
            List<CorpusManifestEntry> entries,
            Dictionary<string, CapturedWireExchange> positiveCaptures,
            Func<TClient, Task> invokeAsync)
            where TClient : class
        {
            host.ClearCaptures();

            Binding binding = WireCaptureHost.CreateClientBinding(soap12);
            EndpointAddress address = new EndpointAddress(host.CreateEndpointUri(path));
            ChannelFactory<TClient> factory = new ChannelFactory<TClient>(binding, address);
            TClient channel = factory.CreateChannel();

            try
            {
                ((ICommunicationObject)channel).Open();
                await invokeAsync(channel).ConfigureAwait(false);
                ((ICommunicationObject)channel).Close();
                factory.Close();
            }
            catch
            {
                ((ICommunicationObject)channel).Abort();
                factory.Abort();
                throw;
            }

            CapturedWireExchange capture = host.DequeueSingleCapture();
            positiveCaptures.Add(fixtureId, capture);

            string relReq = relativeBase + ".request.xml";
            string relResp = relativeBase + ".response.xml";
            string relMeta = relativeBase + ".meta.json";

            FixtureMetadata meta = new FixtureMetadata
            {
                FixtureId = fixtureId,
                SoapVersion = soapVersion,
                Serializer = serializer,
                Path = path,
                RequestContentType = capture.RequestContentType,
                RequestSoapAction = capture.RequestSoapAction,
                ResponseStatusCode = capture.ResponseStatusCode,
                ResponseContentType = capture.ResponseContentType,
                RequestSha256 = ComputeSha256Hex(capture.RequestBody),
                ResponseSha256 = ComputeSha256Hex(capture.ResponseBody),
                CoreWcfMapping = coreWcfMapping,
                ExpectedOutcome = expectedOutcome
            };

            if (writeToDisk)
            {
                WriteBytes(Path.Combine(fixturesDirectory, relReq), capture.RequestBody);
                WriteBytes(Path.Combine(fixturesDirectory, relResp), capture.ResponseBody);
                WriteMetadata(Path.Combine(fixturesDirectory, relMeta), meta);
            }

            entries.Add(new CorpusManifestEntry
            {
                FixtureId = fixtureId,
                Category = "positive",
                WireProfile = soapVersion,
                Serializer = serializer,
                ExpectedOutcome = expectedOutcome,
                CoreWcfMapping = coreWcfMapping,
                Files = [relReq, relResp, relMeta],
                Sha256ByFile = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [relReq] = meta.RequestSha256,
                    [relResp] = meta.ResponseSha256
                }
            });
        }

        private static async Task CaptureWsdlArtifactsAsync(
            string fixturesDirectory,
            bool writeToDisk,
            List<CorpusManifestEntry> entries,
            Dictionary<string, byte[]> wsdlArtifacts)
        {
            await CaptureSingleServiceMetadataAsync<EchoDcsService, IEchoDcsService>(
                "/EchoService.svc",
                soap12: false,
                "WSDL-ECHO-SOAP11",
                "Soap11",
                "DataContractSerializer",
                "CoreWCF.Metadata.Tests.BasicHttpSimpleServiceTest.BasicHttpRequestReplyEchoString",
                "wsdl/echo-soap11",
                includeSplitWsdlAndXsds: true,
                fixturesDirectory,
                writeToDisk,
                entries,
                wsdlArtifacts).ConfigureAwait(false);

            await CaptureSingleServiceMetadataAsync<PaymentDcsUrnService, IPaymentDcsUrnService>(
                "/PaymentService.svc",
                soap12: false,
                "WSDL-PAYMENT-DCS-SOAP11",
                "Soap11",
                "DataContractSerializer",
                "CoreWCF.Metadata.Tests.ComplexTypesWithCollectionsService + DataTypesTest",
                "wsdl/payment-dcs-soap11",
                includeSplitWsdlAndXsds: true,
                fixturesDirectory,
                writeToDisk,
                entries,
                wsdlArtifacts).ConfigureAwait(false);

            await CaptureSingleServiceMetadataAsync<PaymentXmlSerializerService, IPaymentXmlSerializerService>(
                "/PaymentXmlService.svc",
                soap12: false,
                "WSDL-PAYMENT-XMLSER-SOAP11",
                "Soap11",
                "XmlSerializer",
                "CoreWCF.Metadata.Tests.XmlSerializerFormatSoapTest",
                "wsdl/payment-xmlser-soap11",
                includeSplitWsdlAndXsds: false,
                fixturesDirectory,
                writeToDisk,
                entries,
                wsdlArtifacts).ConfigureAwait(false);

            await CaptureSingleServiceMetadataAsync<PaymentDcsUrnService, IPaymentDcsUrnService>(
                "/PaymentServiceSoap12.svc",
                soap12: true,
                "WSDL-PAYMENT-DCS-SOAP12",
                "Soap12",
                "DataContractSerializer",
                "CoreWCF.Metadata.Tests.Helpers.WsdlHelper (soap12 extensions)",
                "wsdl/payment-dcs-soap12",
                includeSplitWsdlAndXsds: false,
                fixturesDirectory,
                writeToDisk,
                entries,
                wsdlArtifacts).ConfigureAwait(false);
        }

        private static async Task CaptureSingleServiceMetadataAsync<TService, TContract>(
            string relativePath,
            bool soap12,
            string fixtureId,
            string wireProfile,
            string serializer,
            string coreWcfMapping,
            string outputPrefix,
            bool includeSplitWsdlAndXsds,
            string fixturesDirectory,
            bool writeToDisk,
            List<CorpusManifestEntry> entries,
            Dictionary<string, byte[]> wsdlArtifacts)
            where TService : class
        {
            await using WireCaptureHost host = await WireCaptureHost.StartMetadataHostAsync<TService, TContract>(relativePath, soap12).ConfigureAwait(false);
            using HttpClient httpClient = new HttpClient();

            int port = host.BaseUri.Port;
            List<string> files = [];
            Dictionary<string, string> hashes = new Dictionary<string, string>(StringComparer.Ordinal);

            string singleWsdlUrl = new Uri(host.BaseUri, relativePath + "?singleWsdl").ToString();
            string singleWsdlText = await httpClient.GetStringAsync(singleWsdlUrl).ConfigureAwait(false);
            byte[] singleWsdlBytes = NormalizeMetadataUrls(singleWsdlText, port);
            string relSingle = outputPrefix + ".single.wsdl";
            RecordWsdlArtifact(relSingle, singleWsdlBytes, fixturesDirectory, writeToDisk, wsdlArtifacts, files, hashes);

            if (includeSplitWsdlAndXsds)
            {
                string wsdlUrl = new Uri(host.BaseUri, relativePath + "?wsdl").ToString();
                string wsdlText = await httpClient.GetStringAsync(wsdlUrl).ConfigureAwait(false);
                byte[] wsdlBytes = NormalizeMetadataUrls(wsdlText, port);
                string relWsdl = outputPrefix + ".wsdl";
                RecordWsdlArtifact(relWsdl, wsdlBytes, fixturesDirectory, writeToDisk, wsdlArtifacts, files, hashes);

                Queue<string> pendingQueries = new Queue<string>();
                HashSet<string> seenQueries = new HashSet<string>(StringComparer.Ordinal);
                EnqueueMetadataImports(wsdlText, pendingQueries, seenQueries);

                while (pendingQueries.Count > 0)
                {
                    string query = pendingQueries.Dequeue();
                    string importUrl = new Uri(host.BaseUri, relativePath + "?" + query).ToString();
                    string importText = await httpClient.GetStringAsync(importUrl).ConfigureAwait(false);
                    EnqueueMetadataImports(importText, pendingQueries, seenQueries);

                    byte[] importBytes = NormalizeMetadataUrls(importText, port);
                    string suffix = query.StartsWith("wsdl=", StringComparison.Ordinal)
                        ? query.Substring("wsdl=".Length) + ".wsdl"
                        : query.Substring("xsd=".Length) + ".xsd";
                    string relImport = outputPrefix + "." + suffix;
                    RecordWsdlArtifact(relImport, importBytes, fixturesDirectory, writeToDisk, wsdlArtifacts, files, hashes);
                }
            }

            entries.Add(new CorpusManifestEntry
            {
                FixtureId = fixtureId,
                Category = "wsdl",
                WireProfile = wireProfile,
                Serializer = serializer,
                ExpectedOutcome = "supported",
                CoreWcfMapping = coreWcfMapping,
                Files = files,
                Sha256ByFile = hashes
            });
        }

        private static void EnqueueMetadataImports(string xmlText, Queue<string> pendingQueries, HashSet<string> seenQueries)
        {
            MatchCollection matches = Regex.Matches(xmlText, @"\?((?:wsdl=wsdl\d+|xsd=xsd\d+))");
            foreach (Match match in matches)
            {
                string query = match.Groups[1].Value;
                if (seenQueries.Add(query))
                {
                    pendingQueries.Enqueue(query);
                }
            }
        }

        private static byte[] NormalizeMetadataUrls(string rawXml, int ephemeralPort)
        {
            string normalized = rawXml
                .Replace($"localhost:{ephemeralPort}", "localhost:5000", StringComparison.Ordinal)
                .Replace($"127.0.0.1:{ephemeralPort}", "localhost:5000", StringComparison.Ordinal)
                .Replace("\r\n", "\n", StringComparison.Ordinal);

            return s_utf8NoBom.GetBytes(normalized);
        }

        private static void RecordWsdlArtifact(
            string relativePath,
            byte[] bytes,
            string fixturesDirectory,
            bool writeToDisk,
            Dictionary<string, byte[]> wsdlArtifacts,
            List<string> files,
            Dictionary<string, string> hashes)
        {
            wsdlArtifacts.Add(relativePath, bytes);
            files.Add(relativePath);
            hashes.Add(relativePath, ComputeSha256Hex(bytes));

            if (writeToDisk)
            {
                WriteBytes(Path.Combine(fixturesDirectory, relativePath), bytes);
            }
        }

        private static void WriteBytes(string fullPath, byte[] bytes)
        {
            string? dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllBytes(fullPath, bytes);
        }

        private static void WriteDcsFixture(
            string fixtureId,
            string relativePath,
            Type targetType,
            object instance,
            string coreWcfMapping,
            string fixturesDirectory,
            bool writeToDisk,
            List<CorpusManifestEntry> entries)
        {
            DataContractSerializer serializer = new DataContractSerializer(targetType);
            using MemoryStream ms = new MemoryStream();
            XmlWriterSettings settings = new XmlWriterSettings
            {
                Encoding = s_utf8NoBom,
                Indent = false,
                OmitXmlDeclaration = true
            };
            using (XmlWriter writer = XmlWriter.Create(ms, settings))
            {
                serializer.WriteObject(writer, instance);
            }

            byte[] bytes = ms.ToArray();
            if (writeToDisk)
            {
                WriteBytes(Path.Combine(fixturesDirectory, relativePath), bytes);
            }

            entries.Add(new CorpusManifestEntry
            {
                FixtureId = fixtureId,
                Category = "serializer",
                WireProfile = "PayloadFragment",
                Serializer = "DataContractSerializer",
                ExpectedOutcome = "supported",
                CoreWcfMapping = coreWcfMapping,
                Files = [relativePath],
                Sha256ByFile = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [relativePath] = ComputeSha256Hex(bytes)
                }
            });
        }

        private static void WriteMetadata(string fullPath, FixtureMetadata metadata)
        {
            string? dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(metadata, s_jsonOptions) + "\n";
            File.WriteAllText(fullPath, json, s_utf8NoBom);
        }

        private static void WriteXmlSerializerFixture(
            string fixtureId,
            string relativePath,
            Type targetType,
            object instance,
            string coreWcfMapping,
            string fixturesDirectory,
            bool writeToDisk,
            List<CorpusManifestEntry> entries)
        {
            XmlSerializer serializer = new XmlSerializer(targetType);
            using MemoryStream ms = new MemoryStream();
            XmlWriterSettings settings = new XmlWriterSettings
            {
                Encoding = s_utf8NoBom,
                Indent = false,
                OmitXmlDeclaration = true
            };
            using (XmlWriter writer = XmlWriter.Create(ms, settings))
            {
                serializer.Serialize(writer, instance);
            }

            byte[] bytes = ms.ToArray();
            if (writeToDisk)
            {
                WriteBytes(Path.Combine(fixturesDirectory, relativePath), bytes);
            }

            entries.Add(new CorpusManifestEntry
            {
                FixtureId = fixtureId,
                Category = "serializer",
                WireProfile = "PayloadFragment",
                Serializer = "XmlSerializer",
                ExpectedOutcome = "supported",
                CoreWcfMapping = coreWcfMapping,
                Files = [relativePath],
                Sha256ByFile = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [relativePath] = ComputeSha256Hex(bytes)
                }
            });
        }

        #endregion
    }
}
