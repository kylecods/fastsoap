using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Schema;

using FastSoap.R0G.Oracle.Infrastructure;

using Xunit;

namespace FastSoap.R0G.Oracle
{
    /// <summary>
    /// Shared xUnit class fixture that executes the CoreWCF 1.9.2 + WCF <c>System.ServiceModel.Http</c>
    /// oracle capture once per test run.
    /// </summary>
    public sealed class OracleRunFixture : IAsyncLifetime
    {
        #region Properties

        /// <summary>
        /// Gets the path to <c>tests/fixtures</c>.
        /// </summary>
        public string FixturesDirectory { get; private set; } = string.Empty;

        /// <summary>
        /// Gets the deserialized <c>manifest.json</c> loaded from disk.
        /// </summary>
        public CorpusManifest Manifest { get; private set; } = null!;

        /// <summary>
        /// Gets the live oracle capture result.
        /// </summary>
        public OracleRunResult Result { get; private set; } = null!;

        #endregion

        #region Public Methods

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            string repoRoot = OracleFixtureGenerator.FindRepositoryRoot();
            FixturesDirectory = Path.Combine(repoRoot, "tests", "fixtures");
            string manifestPath = Path.Combine(FixturesDirectory, "manifest.json");

            bool shouldGenerate = !File.Exists(manifestPath)
                || string.Equals(Environment.GetEnvironmentVariable("FASTSOAP_REGENERATE_FIXTURES"), "1", StringComparison.Ordinal);

            Result = await OracleFixtureGenerator.RunAsync(FixturesDirectory, writeToDisk: shouldGenerate).ConfigureAwait(false);

            string manifestJson = await File.ReadAllTextAsync(manifestPath, Encoding.UTF8).ConfigureAwait(false);
            Manifest = JsonSerializer.Deserialize<CorpusManifest>(manifestJson, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }) ?? throw new InvalidOperationException("Failed to deserialize manifest.json.");
        }

        #endregion
    }

    /// <summary>
    /// Verifies that every immutable golden fixture under <c>tests/fixtures/</c> matches live
    /// CoreWCF 1.9.2 + WCF <c>System.ServiceModel.Http</c> + BCL serializers and passes WSDL/XSD validation.
    /// </summary>
    public sealed class OracleVerificationTests : IClassFixture<OracleRunFixture>
    {
        #region Fields

        private static readonly JsonSerializerOptions s_jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly OracleRunFixture _fixture;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of <see cref="OracleVerificationTests"/>.
        /// </summary>
        /// <param name="fixture">Shared oracle capture fixture.</param>
        public OracleVerificationTests(OracleRunFixture fixture)
        {
            ArgumentNullException.ThrowIfNull(fixture);

            _fixture = fixture;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Verifies that every file listed in <c>manifest.json</c> exists on disk and matches its recorded SHA-256 digest.
        /// </summary>
        [Fact]
        public async Task ManifestEntriesAndChecksumsMatchDiskAsync()
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            Assert.Equal(_fixture.Result.Entries.Count, _fixture.Manifest.Entries.Count);

            foreach (CorpusManifestEntry entry in _fixture.Manifest.Entries)
            {
                foreach (string relFile in entry.Files)
                {
                    string fullPath = Path.Combine(_fixture.FixturesDirectory, relFile);
                    Assert.True(File.Exists(fullPath), $"Missing fixture file: {relFile}");

                    if (entry.Sha256ByFile.TryGetValue(relFile, out string? expectedSha))
                    {
                        byte[] diskBytes = await File.ReadAllBytesAsync(fullPath, ct);
                        string actualSha = OracleFixtureGenerator.ComputeSha256Hex(diskBytes);
                        Assert.Equal(expectedSha, actualSha);
                    }
                }
            }
        }

        /// <summary>
        /// Verifies that every negative/adversarial fixture matches live CoreWCF 1.9.2 HTTP status and response bytes.
        /// </summary>
        [Fact]
        public async Task NegativeAdversarialFixturesMatchLiveCoreWcfAsync()
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            IReadOnlyList<AdversarialTestCase> cases = AdversarialCases.GetAll();
            Assert.Equal(27, cases.Count);

            foreach (AdversarialTestCase negativeCase in cases)
            {
                CapturedWireExchange observed = _fixture.Result.NegativeCaptures[negativeCase.FixtureId];
                CorpusManifestEntry entry = _fixture.Manifest.Entries.Single(e => string.Equals(e.FixtureId, negativeCase.FixtureId, StringComparison.Ordinal));
                byte[] diskReq = await File.ReadAllBytesAsync(Path.Combine(_fixture.FixturesDirectory, entry.Files[0]), ct);
                byte[] diskResp = await File.ReadAllBytesAsync(Path.Combine(_fixture.FixturesDirectory, entry.Files[1]), ct);
                string metaJson = await File.ReadAllTextAsync(Path.Combine(_fixture.FixturesDirectory, entry.Files[2]), Encoding.UTF8, ct);
                FixtureMetadata? meta = JsonSerializer.Deserialize<FixtureMetadata>(metaJson, s_jsonOptions);

                Assert.NotNull(meta);
                Assert.Equal(negativeCase.ExpectedCoreWcfStatusCode, observed.ResponseStatusCode);
                Assert.Equal(diskReq, observed.RequestBody);
                Assert.Equal(diskResp, observed.ResponseBody);
                Assert.Equal(meta.ResponseStatusCode, observed.ResponseStatusCode);
            }
        }

        /// <summary>
        /// Verifies that every positive SOAP 1.1 and SOAP 1.2 fixture matches live WCF client + CoreWCF 1.9.2 server bytes.
        /// </summary>
        [Fact]
        public async Task PositiveSoap11AndSoap12FixturesMatchLiveCoreWcfAsync()
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            Assert.Equal(20, _fixture.Result.PositiveCaptures.Count);

            foreach (KeyValuePair<string, CapturedWireExchange> pair in _fixture.Result.PositiveCaptures)
            {
                CorpusManifestEntry entry = _fixture.Manifest.Entries.Single(e => string.Equals(e.FixtureId, pair.Key, StringComparison.Ordinal));
                byte[] diskReq = await File.ReadAllBytesAsync(Path.Combine(_fixture.FixturesDirectory, entry.Files[0]), ct);
                byte[] diskResp = await File.ReadAllBytesAsync(Path.Combine(_fixture.FixturesDirectory, entry.Files[1]), ct);
                string metaJson = await File.ReadAllTextAsync(Path.Combine(_fixture.FixturesDirectory, entry.Files[2]), Encoding.UTF8, ct);
                FixtureMetadata? meta = JsonSerializer.Deserialize<FixtureMetadata>(metaJson, s_jsonOptions);

                Assert.NotNull(meta);
                Assert.Equal(diskReq, pair.Value.RequestBody);
                Assert.Equal(diskResp, pair.Value.ResponseBody);
                Assert.Equal(meta.ResponseStatusCode, pair.Value.ResponseStatusCode);
                Assert.Equal(meta.RequestSoapAction, pair.Value.RequestSoapAction);
            }
        }

        /// <summary>
        /// Verifies that every captured WSDL 1.1 and XSD fixture matches live CoreWCF metadata and parses cleanly without DTDs.
        /// </summary>
        [Fact]
        public async Task WsdlAndXsdFixturesMatchLiveCoreWcfAndValidateAsync()
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            Assert.True(_fixture.Result.WsdlArtifacts.Count >= 9);

            foreach (KeyValuePair<string, byte[]> wsdlPair in _fixture.Result.WsdlArtifacts)
            {
                byte[] diskWsdl = await File.ReadAllBytesAsync(Path.Combine(_fixture.FixturesDirectory, wsdlPair.Key), ct);
                Assert.Equal(diskWsdl, wsdlPair.Value);

                ValidateWellFormedXmlAndSchemas(wsdlPair.Key, diskWsdl);
            }
        }

        #endregion

        #region Private Methods

        private static void ValidateWellFormedXmlAndSchemas(string relativePath, byte[] xmlBytes)
        {
            using MemoryStream ms = new MemoryStream(xmlBytes);
            XmlReaderSettings settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };
            using XmlReader reader = XmlReader.Create(ms, settings);
            XmlDocument doc = new XmlDocument
            {
                XmlResolver = null
            };
            doc.Load(reader);

            Assert.NotNull(doc.DocumentElement);

            if (relativePath.EndsWith(".xsd", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Equal("schema", doc.DocumentElement.LocalName);
                Assert.Equal(XmlSchema.Namespace, doc.DocumentElement.NamespaceURI);
            }
            else if (relativePath.EndsWith(".wsdl", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Equal("definitions", doc.DocumentElement.LocalName);
                Assert.Equal("http://schemas.xmlsoap.org/wsdl/", doc.DocumentElement.NamespaceURI);
            }
        }

        #endregion
    }
}
