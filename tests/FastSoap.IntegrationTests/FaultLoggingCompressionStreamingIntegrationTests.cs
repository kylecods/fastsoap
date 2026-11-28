using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using CloudNimble.Text.Xml;
using CloudNimble.Text.Xml.Serialization;

using FastSoap.Abstractions;
using FastSoap.AspNetCore;
using FastSoap.Core.Buffers;
using FastSoap.Core.Serialization;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Xunit;

namespace FastSoap.IntegrationTests
{

    [XmlRoot(ElementName = "LargeBatch", Namespace = "urn:compress")]
    public sealed class LargeBatchRequestWire
    {

        #region Properties

        [XmlElement(ElementName = "Payload", Namespace = "urn:compress")]
        public string Payload { get; set; } = string.Empty;

        #endregion

    }

    [XmlRoot(ElementName = "LargeBatchResponse", Namespace = "urn:compress")]
    public sealed class LargeBatchResponseWire
    {

        #region Properties

        [XmlElement(ElementName = "Items", Namespace = "urn:compress")]
        public List<string> Items { get; set; } = [];

        #endregion

    }

    [XmlRoot(ElementName = "ThrowSerialize", Namespace = "urn:faults")]
    public sealed class ThrowSerializeRequestWire
    {

        #region Properties

        [XmlElement(ElementName = "Message", Namespace = "urn:faults")]
        public string Message { get; set; } = string.Empty;

        #endregion

    }

    [XmlRoot(ElementName = "ThrowInvalidDataSerialize", Namespace = "urn:faults")]
    public sealed class ThrowInvalidDataSerializeRequestWire
    {

        #region Properties

        [XmlElement(ElementName = "Message", Namespace = "urn:faults")]
        public string Message { get; set; } = string.Empty;

        #endregion

    }

    [XmlRoot(ElementName = "ThrowInvalidDataOnSerialize", Namespace = "urn:faults")]
    public sealed class ThrowInvalidDataOnSerializeResponseWire
    {

        #region Properties

        [XmlElement(ElementName = "Head", Namespace = "urn:faults", Order = 0)]
        public string Head => "PARTIAL_PREFIX";

        [XmlElement(ElementName = "Tail", Namespace = "urn:faults", Order = 1)]
        public string Tail => throw new InvalidDataException("SECRET_INVALID_DATA_POST_START");

        #endregion

    }

    [XmlRoot(ElementName = "ThrowOnSerialize", Namespace = "urn:faults")]
    public sealed class ThrowOnSerializeResponseWire
    {

        #region Properties

        [XmlElement(ElementName = "Head", Namespace = "urn:faults", Order = 0)]
        public string Head => "PARTIAL_PREFIX";

        [XmlElement(ElementName = "Tail", Namespace = "urn:faults", Order = 1)]
        public string Tail => throw new InvalidOperationException("SECRET_SERIALIZE_FAILURE_TOKEN");

        #endregion

    }

    [XmlRoot(ElementName = "IntegrationFaultDetail", Namespace = "urn:faults")]
    public sealed class IntegrationFaultDetailWire
    {

        #region Properties

        [XmlElement(ElementName = "Code", Namespace = "urn:faults", Order = 0)]
        public string Code { get; set; } = string.Empty;

        [XmlElement(ElementName = "Message", Namespace = "urn:faults", Order = 1)]
        public string Message { get; set; } = string.Empty;

        #endregion

    }

    [XmlSerializable<EchoRequest>]
    [XmlSerializable<EchoResponse>]
    [XmlSerializable<IntegrationFaultDetailWire>]
    [XmlSerializable<LargeBatchRequestWire>]
    [XmlSerializable<LargeBatchResponseWire>]
    [XmlSerializable<ThrowInvalidDataOnSerializeResponseWire>]
    [XmlSerializable<ThrowInvalidDataSerializeRequestWire>]
    [XmlSerializable<ThrowOnSerializeResponseWire>]
    [XmlSerializable<ThrowSerializeRequestWire>]
    public partial class ExtendedIntegrationXmlContext : XmlSerializerContext
    {
    }

    /// <summary>
    /// Real Kestrel integration tests for exception logging on generic faults, structured fault details,
    /// compressed HTTP request/response encodings, and opt-in direct response streaming.
    /// </summary>
    public sealed class FaultLoggingCompressionStreamingIntegrationTests
    {

        #region Fields

        private static readonly SoapOperationDescriptor EchoDescriptor = new SoapOperationDescriptor(
            "Echo",
            "urn:echo/IEchoService/Echo",
            "urn:echo/IEchoService/EchoResponse",
            new SoapQName("urn:echo", "Echo"),
            new SoapQName("urn:echo", "EchoResponse"));

        private static readonly SoapOperationDescriptor LargeBatchDescriptor = new SoapOperationDescriptor(
            "LargeBatch",
            "urn:compress/LargeBatch",
            "urn:compress/LargeBatchResponse",
            new SoapQName("urn:compress", "LargeBatch"),
            new SoapQName("urn:compress", "LargeBatchResponse"));

        private static readonly SoapOperationDescriptor ThrowInvalidDataSerializeDescriptor = new SoapOperationDescriptor(
            "ThrowInvalidDataSerialize",
            "urn:faults/ThrowInvalidDataSerialize",
            "urn:faults/ThrowInvalidDataSerializeResponse",
            new SoapQName("urn:faults", "ThrowInvalidDataSerialize"),
            new SoapQName("urn:faults", "ThrowInvalidDataOnSerialize"));

        private static readonly SoapOperationDescriptor ThrowSerializeDescriptor = new SoapOperationDescriptor(
            "ThrowSerialize",
            "urn:faults/ThrowSerialize",
            "urn:faults/ThrowSerializeResponse",
            new SoapQName("urn:faults", "ThrowSerialize"),
            new SoapQName("urn:faults", "ThrowOnSerialize"));

        #endregion

        #region Public Methods

        [Fact]
        public async Task CompressedRequests_GzipBrotliDeflateDisabledUnknownAndBomb_EnforceStatusAndZeroInvocations()
        {
            await using IntegrationHarness harness = await IntegrationHarness.StartAsync();

            byte[] validRequest = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><Echo xmlns=\"urn:echo\"><Message>compressed-ok</Message></Echo></s:Body></s:Envelope>"u8.ToArray();
            const string expectedResponse = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><s:Body><EchoResponse xmlns=\"urn:echo\"><EchoResult>compressed-ok</EchoResult></EchoResponse></s:Body></s:Envelope>";

            foreach (SoapCompressionEncoding encoding in new[]
            {
                SoapCompressionEncoding.Gzip,
                SoapCompressionEncoding.Brotli,
                SoapCompressionEncoding.Deflate
            })
            {
                string token = SoapCompressionCodec.ToHeaderValue(encoding);
                byte[] compressedBytes = CompressBytes(validRequest, encoding);

                using HttpRequestMessage okReq = CreateCompressedSoap11Request("/compressed.svc", "urn:echo/IEchoService/Echo", compressedBytes, token);
                using HttpResponseMessage okRes = await harness.Client.SendAsync(okReq, TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.OK, okRes.StatusCode);
                Assert.Equal(expectedResponse, await okRes.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

                using HttpRequestMessage disabledReq = CreateCompressedSoap11Request("/default.svc", "urn:echo/IEchoService/Echo", compressedBytes, token);
                using HttpResponseMessage disabledRes = await harness.Client.SendAsync(disabledReq, TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.UnsupportedMediaType, disabledRes.StatusCode);
            }

            Assert.Equal(3, harness.ServiceInvocations);

            using (HttpRequestMessage unknownReq = CreateCompressedSoap11Request("/compressed.svc", "urn:echo/IEchoService/Echo", validRequest, "zstd"))
            using (HttpResponseMessage unknownRes = await harness.Client.SendAsync(unknownReq, TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.UnsupportedMediaType, unknownRes.StatusCode);
            }

            using (HttpRequestMessage corruptReq = CreateCompressedSoap11Request("/compressed.svc", "urn:echo/IEchoService/Echo", "corrupt-gzip"u8.ToArray(), "gzip"))
            using (HttpResponseMessage corruptRes = await harness.Client.SendAsync(corruptReq, TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.BadRequest, corruptRes.StatusCode);
            }

            byte[] bombXml = Encoding.UTF8.GetBytes($"<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><Echo xmlns=\"urn:echo\"><Message>{new string('B', 32768)}</Message></Echo></s:Body></s:Envelope>");
            byte[] compressedBomb = CompressBytes(bombXml, SoapCompressionEncoding.Brotli);
            Assert.True(compressedBomb.Length < 4096);

            using (HttpRequestMessage bombReq = CreateCompressedSoap11Request("/compressed-quota.svc", "urn:echo/IEchoService/Echo", compressedBomb, "br"))
            using (HttpResponseMessage bombRes = await harness.Client.SendAsync(bombReq, TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.RequestEntityTooLarge, bombRes.StatusCode);
            }

            Assert.Equal(3, harness.ServiceInvocations);
        }

        [Fact]
        public async Task CompressedRequests_NonPowerOfTwoQuota_Rejects6000AllowsExact5000AndRejectsTrailingBrotliGarbage()
        {
            await using IntegrationHarness harness = await IntegrationHarness.StartAsync();

            const string envelopePrefix = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><Echo xmlns=\"urn:echo\"><Message>";
            const string envelopeSuffix = "</Message></Echo></s:Body></s:Envelope>";
            int messageLen5000 = 5000 - envelopePrefix.Length - envelopeSuffix.Length;
            string exactMessage5000 = new string('Q', messageLen5000);
            byte[] exactXml5000 = Encoding.UTF8.GetBytes($"{envelopePrefix}{exactMessage5000}{envelopeSuffix}");
            Assert.Equal(5000, exactXml5000.Length);

            string overMessage6000 = new string('O', 6000 - envelopePrefix.Length - envelopeSuffix.Length);
            byte[] overXml6000 = Encoding.UTF8.GetBytes($"{envelopePrefix}{overMessage6000}{envelopeSuffix}");
            Assert.Equal(6000, overXml6000.Length);

            string expectedExactResponse = $"<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><s:Body><EchoResponse xmlns=\"urn:echo\"><EchoResult>{exactMessage5000}</EchoResult></EchoResponse></s:Body></s:Envelope>";

            foreach (SoapCompressionEncoding encoding in new[]
            {
                SoapCompressionEncoding.Brotli,
                SoapCompressionEncoding.Gzip,
                SoapCompressionEncoding.Deflate
            })
            {
                string token = SoapCompressionCodec.ToHeaderValue(encoding);
                byte[] overCompressed = CompressBytes(overXml6000, encoding);
                Assert.True(overCompressed.Length < 5000);

                int beforeOverflow = harness.ServiceInvocations;
                using (HttpRequestMessage overReq = CreateCompressedSoap11Request("/compressed-non-pow2-quota.svc", "urn:echo/IEchoService/Echo", overCompressed, token))
                using (HttpResponseMessage overRes = await harness.Client.SendAsync(overReq, TestContext.Current.CancellationToken))
                {
                    Assert.Equal(HttpStatusCode.RequestEntityTooLarge, overRes.StatusCode);
                }

                Assert.Equal(beforeOverflow, harness.ServiceInvocations);

                byte[] exactCompressed = CompressBytes(exactXml5000, encoding);
                using (HttpRequestMessage exactReq = CreateCompressedSoap11Request("/compressed-non-pow2-quota.svc", "urn:echo/IEchoService/Echo", exactCompressed, token))
                using (HttpResponseMessage exactRes = await harness.Client.SendAsync(exactReq, TestContext.Current.CancellationToken))
                {
                    Assert.Equal(HttpStatusCode.OK, exactRes.StatusCode);
                    Assert.Equal(expectedExactResponse, await exactRes.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
                }

                Assert.Equal(beforeOverflow + 1, harness.ServiceInvocations);

                if (encoding == SoapCompressionEncoding.Brotli)
                {
                    byte[] trailingGarbage = new byte[exactCompressed.Length + 2];
                    exactCompressed.CopyTo(trailingGarbage, 0);
                    trailingGarbage[^2] = 0x00;
                    trailingGarbage[^1] = 0xFF;

                    using HttpRequestMessage garbageReq = CreateCompressedSoap11Request("/compressed-non-pow2-quota.svc", "urn:echo/IEchoService/Echo", trailingGarbage, token);
                    using HttpResponseMessage garbageRes = await harness.Client.SendAsync(garbageReq, TestContext.Current.CancellationToken);

                    Assert.Equal(HttpStatusCode.BadRequest, garbageRes.StatusCode);
                    Assert.Equal(beforeOverflow + 1, harness.ServiceInvocations);
                }
            }
        }

        [Fact]
        public async Task CompressedResponses_HonorsAcceptEncodingThresholdVaryAndReducesWireBytes()
        {
            await using IntegrationHarness harness = await IntegrationHarness.StartAsync();

            byte[] batchReqBytes = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><LargeBatch xmlns=\"urn:compress\"><Payload>seed</Payload></LargeBatch></s:Body></s:Envelope>"u8.ToArray();

            byte[] uncompressedWire;
            using (HttpRequestMessage plainReq = CreateSoap11Request("/compressed-response.svc", "urn:compress/LargeBatch", batchReqBytes))
            using (HttpResponseMessage plainRes = await harness.Client.SendAsync(plainReq, TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, plainRes.StatusCode);
                Assert.Empty(plainRes.Content.Headers.ContentEncoding);
                Assert.Contains("Accept-Encoding", plainRes.Headers.Vary, StringComparer.OrdinalIgnoreCase);
                uncompressedWire = await plainRes.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
                Assert.Equal(uncompressedWire.Length, plainRes.Content.Headers.ContentLength);
            }

            foreach ((string acceptEncoding, SoapCompressionEncoding expectedEncoding) in new[]
            {
                ("gzip, deflate, br", SoapCompressionEncoding.Brotli),
                ("gzip", SoapCompressionEncoding.Gzip),
                ("deflate", SoapCompressionEncoding.Deflate)
            })
            {
                string expectedToken = SoapCompressionCodec.ToHeaderValue(expectedEncoding);
                using HttpRequestMessage req = CreateSoap11Request("/compressed-response.svc", "urn:compress/LargeBatch", batchReqBytes);
                req.Headers.TryAddWithoutValidation("Accept-Encoding", acceptEncoding);

                using HttpResponseMessage res = await harness.Client.SendAsync(req, TestContext.Current.CancellationToken);
                Assert.Equal(HttpStatusCode.OK, res.StatusCode);
                Assert.Equal([expectedToken], res.Content.Headers.ContentEncoding);
                Assert.Contains("Accept-Encoding", res.Headers.Vary, StringComparer.OrdinalIgnoreCase);

                byte[] compressedWire = await res.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
                Assert.Equal(compressedWire.Length, res.Content.Headers.ContentLength);
                Assert.True(compressedWire.Length < uncompressedWire.Length / 4);

                byte[] decompressed = DecompressBytes(compressedWire, expectedEncoding);
                Assert.True(decompressed.AsSpan().SequenceEqual(uncompressedWire));
            }

            byte[] smallEchoReq = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><Echo xmlns=\"urn:echo\"><Message>tiny</Message></Echo></s:Body></s:Envelope>"u8.ToArray();
            using (HttpRequestMessage belowThresholdReq = CreateSoap11Request("/compressed-response.svc", "urn:echo/IEchoService/Echo", smallEchoReq))
            {
                belowThresholdReq.Headers.TryAddWithoutValidation("Accept-Encoding", "br, gzip");
                using HttpResponseMessage belowThresholdRes = await harness.Client.SendAsync(belowThresholdReq, TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.OK, belowThresholdRes.StatusCode);
                Assert.Empty(belowThresholdRes.Content.Headers.ContentEncoding);
                Assert.Contains("Accept-Encoding", belowThresholdRes.Headers.Vary, StringComparer.OrdinalIgnoreCase);
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><s:Body><EchoResponse xmlns=\"urn:echo\"><EchoResult>tiny</EchoResult></EchoResponse></s:Body></s:Envelope>",
                    await belowThresholdRes.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            }
        }

        [Fact]
        public async Task CompressedResponses_IncompressiblePayloadNearMaxResponseBytes_FallsBackTo200UncompressedWithVary()
        {
            await using IntegrationHarness harness = await IntegrationHarness.StartAsync();

            Random rng = new Random(42);
            StringBuilder sb = new StringBuilder(100);
            List<string> expectedItems = new List<string>(30);
            for (int itemIndex = 0; itemIndex < 30; itemIndex++)
            {
                sb.Clear();
                for (int charIndex = 0; charIndex < 100; charIndex++)
                {
                    int kind = rng.Next(4);
                    if (kind == 0)
                    {
                        char ch = (char)rng.Next(0x21, 0x7E);
                        if (ch is '<' or '>' or '&' or '|')
                        {
                            ch = 'X';
                        }

                        sb.Append(ch);
                    }
                    else if (kind == 1)
                    {
                        sb.Append((char)rng.Next(0x00A1, 0x0500));
                    }
                    else if (kind == 2)
                    {
                        sb.Append((char)rng.Next(0x4E00, 0x9FA0));
                    }
                    else
                    {
                        sb.Append(char.ConvertFromUtf32(rng.Next(0x10000, 0x10FFFF)));
                    }
                }

                expectedItems.Add(sb.ToString());
            }

            string payloadSeed = string.Join('|', expectedItems);
            byte[] reqBytes = Encoding.UTF8.GetBytes(
                $"<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><LargeBatch xmlns=\"urn:compress\"><Payload>{payloadSeed}</Payload></LargeBatch></s:Body></s:Envelope>");

            string expectedBodyItems = string.Concat(expectedItems.Select(static item => $"<Items>{item}</Items>"));
            string expectedResponse =
                $"<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><s:Body><LargeBatchResponse xmlns=\"urn:compress\">{expectedBodyItems}</LargeBatchResponse></s:Body></s:Envelope>";

            using (HttpRequestMessage plainReq = CreateSoap11Request("/tight-compressed-response.svc", "urn:compress/LargeBatch", reqBytes))
            using (HttpResponseMessage plainRes = await harness.Client.SendAsync(plainReq, TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, plainRes.StatusCode);
                Assert.Equal(expectedResponse, await plainRes.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            }

            foreach (string acceptEncoding in new[] { "gzip", "deflate" })
            {
                using HttpRequestMessage req = CreateSoap11Request("/tight-compressed-response.svc", "urn:compress/LargeBatch", reqBytes);
                req.Headers.TryAddWithoutValidation("Accept-Encoding", acceptEncoding);

                using HttpResponseMessage res = await harness.Client.SendAsync(req, TestContext.Current.CancellationToken);
                Assert.Equal(HttpStatusCode.OK, res.StatusCode);
                Assert.Empty(res.Content.Headers.ContentEncoding);
                Assert.Contains("Accept-Encoding", res.Headers.Vary, StringComparer.OrdinalIgnoreCase);
                Assert.Equal(expectedResponse, await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            }
        }

        [Fact]
        public async Task DirectResponseStreaming_PostStartInvalidDataException_LogsErrorAndAborts()
        {
            await using IntegrationHarness harness = await IntegrationHarness.StartAsync();
            harness.LoggerProvider.Clear();

            byte[] reqBytes = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><ThrowInvalidDataSerialize xmlns=\"urn:faults\"><Message>SECRET_INVALID_DATA_REQ</Message></ThrowInvalidDataSerialize></s:Body></s:Envelope>"u8.ToArray();
            using HttpRequestMessage req = CreateSoap11Request("/direct-streaming.svc", "urn:faults/ThrowInvalidDataSerialize", reqBytes);

            bool threwOrAborted = false;
            try
            {
                using HttpResponseMessage res = await harness.Client.SendAsync(req, TestContext.Current.CancellationToken);
                string body = await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
                Assert.DoesNotContain("<s:Fault>", body, StringComparison.Ordinal);
                Assert.DoesNotContain("SECRET_INVALID_DATA_POST_START", body, StringComparison.Ordinal);
            }
            catch (HttpRequestException)
            {
                threwOrAborted = true;
            }
            catch (IOException)
            {
                threwOrAborted = true;
            }

            Assert.True(threwOrAborted);

            RecordedLogEntry errorLog = Assert.Single(
                harness.LoggerProvider.Entries,
                static e => e.LogLevel == LogLevel.Error && e.EventId.Id == 1007);
            Assert.IsType<InvalidDataException>(errorLog.Exception);
            Assert.Equal("SECRET_INVALID_DATA_POST_START", errorLog.Exception.Message);
            Assert.DoesNotContain("SECRET_INVALID_DATA_REQ", errorLog.FormattedMessage, StringComparison.Ordinal);
        }

        [Fact]
        public async Task DirectResponseStreaming_SucceedsAndNeverAppendsDoubleEnvelopeWhenSerializationFailsAfterStart()
        {
            await using IntegrationHarness harness = await IntegrationHarness.StartAsync();

            byte[] okEchoReq = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><Echo xmlns=\"urn:echo\"><Message>streamed-ok</Message></Echo></s:Body></s:Envelope>"u8.ToArray();
            using (HttpRequestMessage req = CreateSoap11Request("/direct-streaming.svc", "urn:echo/IEchoService/Echo", okEchoReq))
            using (HttpResponseMessage res = await harness.Client.SendAsync(req, TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, res.StatusCode);
                Assert.Equal("text/xml; charset=utf-8", res.Content.Headers.ContentType?.ToString());
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><s:Body><EchoResponse xmlns=\"urn:echo\"><EchoResult>streamed-ok</EchoResult></EchoResponse></s:Body></s:Envelope>",
                    await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            }

            byte[] faultBeforeStartReq = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><Echo xmlns=\"urn:echo\"><Message>__THROW_FAULT__</Message></Echo></s:Body></s:Envelope>"u8.ToArray();
            using (HttpRequestMessage faultReq = CreateSoap11Request("/direct-streaming.svc", "urn:echo/IEchoService/Echo", faultBeforeStartReq))
            using (HttpResponseMessage faultRes = await harness.Client.SendAsync(faultReq, TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.InternalServerError, faultRes.StatusCode);
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault><faultcode>s:Client</faultcode><faultstring>Handled fault before streaming.</faultstring><detail><IntegrationFaultDetail xmlns=\"urn:faults\"><Code>PRE_START</Code><Message>Rejected before start.</Message></IntegrationFaultDetail></detail></s:Fault></s:Body></s:Envelope>",
                    await faultRes.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            }

            harness.LoggerProvider.Clear();

            byte[] failAfterStartReq = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><ThrowSerialize xmlns=\"urn:faults\"><Message>SECRET_STREAM_PAYLOAD</Message></ThrowSerialize></s:Body></s:Envelope>"u8.ToArray();
            using HttpRequestMessage failReq = CreateSoap11Request("/direct-streaming.svc", "urn:faults/ThrowSerialize", failAfterStartReq);

            bool threwOrAborted = false;
            try
            {
                using HttpResponseMessage failRes = await harness.Client.SendAsync(failReq, TestContext.Current.CancellationToken);
                string body = await failRes.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
                Assert.DoesNotContain("<s:Fault>", body, StringComparison.Ordinal);
                Assert.DoesNotContain("SECRET_SERIALIZE_FAILURE_TOKEN", body, StringComparison.Ordinal);
            }
            catch (HttpRequestException)
            {
                threwOrAborted = true;
            }
            catch (IOException)
            {
                threwOrAborted = true;
            }

            Assert.True(threwOrAborted);

            RecordedLogEntry errorLog = Assert.Single(
                harness.LoggerProvider.Entries,
                static e => e.LogLevel == LogLevel.Error && e.EventId.Id == 1007);
            Assert.NotNull(errorLog.Exception);
            Assert.Equal("SECRET_SERIALIZE_FAILURE_TOKEN", errorLog.Exception.Message);
            Assert.DoesNotContain("SECRET_STREAM_PAYLOAD", errorLog.FormattedMessage, StringComparison.Ordinal);
        }

        [Fact]
        public async Task GenericFaultExceptions_HeaderHandlerAndResponseSerialization_LogExactExceptionAtErrorWithoutLeakingPayload()
        {
            await using IntegrationHarness harness = await IntegrationHarness.StartAsync();

            byte[] headerThrowReq = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Header><h:FailHeader xmlns:h=\"urn:faults\">SENSITIVE_HEADER_SECRET</h:FailHeader></s:Header><s:Body><Echo xmlns=\"urn:echo\"><Message>SENSITIVE_BODY_SECRET</Message></Echo></s:Body></s:Envelope>"u8.ToArray();
            harness.LoggerProvider.Clear();

            using (HttpRequestMessage req = CreateSoap11Request("/logging-faults.svc", "urn:echo/IEchoService/Echo", headerThrowReq))
            using (HttpResponseMessage res = await harness.Client.SendAsync(req, TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);
                string xml = await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
                Assert.DoesNotContain("HEADER_DB_TOKEN", xml, StringComparison.Ordinal);
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault><faultcode>s:Server</faultcode><faultstring>An internal server error occurred while processing the SOAP request.</faultstring></s:Fault></s:Body></s:Envelope>",
                    xml);
            }

            RecordedLogEntry headerError = Assert.Single(
                harness.LoggerProvider.Entries,
                static e => e.LogLevel == LogLevel.Error && e.EventId.Id == 1008);
            Assert.NotNull(headerError.Exception);
            Assert.Equal("HEADER_DB_TOKEN", headerError.Exception.Message);
            Assert.Contains("FailureStage=header_processing", headerError.FormattedMessage, StringComparison.Ordinal);
            Assert.DoesNotContain("SENSITIVE_HEADER_SECRET", headerError.FormattedMessage, StringComparison.Ordinal);
            Assert.DoesNotContain("SENSITIVE_BODY_SECRET", headerError.FormattedMessage, StringComparison.Ordinal);

            byte[] handlerThrowReq = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><Echo xmlns=\"urn:echo\"><Message>__THROW_INTERNAL__</Message></Echo></s:Body></s:Envelope>"u8.ToArray();
            harness.LoggerProvider.Clear();

            using (HttpRequestMessage req = CreateSoap11Request("/logging-faults.svc", "urn:echo/IEchoService/Echo", handlerThrowReq))
            using (HttpResponseMessage res = await harness.Client.SendAsync(req, TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);
                string xml = await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
                Assert.DoesNotContain("HANDLER_SECRET_KEY", xml, StringComparison.Ordinal);
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault><faultcode>s:Server</faultcode><faultstring>An internal server error occurred while processing the SOAP request.</faultstring></s:Fault></s:Body></s:Envelope>",
                    xml);
            }

            RecordedLogEntry handlerError = Assert.Single(
                harness.LoggerProvider.Entries,
                static e => e.LogLevel == LogLevel.Error && e.EventId.Id == 1008);
            Assert.NotNull(handlerError.Exception);
            Assert.Equal("HANDLER_SECRET_KEY", handlerError.Exception.Message);
            Assert.Contains("FailureStage=handler", handlerError.FormattedMessage, StringComparison.Ordinal);
            Assert.DoesNotContain("__THROW_INTERNAL__", handlerError.FormattedMessage, StringComparison.Ordinal);

            byte[] serThrowReq = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><ThrowSerialize xmlns=\"urn:faults\"><Message>SENSITIVE_BODY_SECRET</Message></ThrowSerialize></s:Body></s:Envelope>"u8.ToArray();
            harness.LoggerProvider.Clear();

            using (HttpRequestMessage req = CreateSoap11Request("/logging-faults.svc", "urn:faults/ThrowSerialize", serThrowReq))
            using (HttpResponseMessage res = await harness.Client.SendAsync(req, TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);
                string xml = await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
                Assert.DoesNotContain("SECRET_SERIALIZE_FAILURE_TOKEN", xml, StringComparison.Ordinal);
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault><faultcode>s:Server</faultcode><faultstring>An internal server error occurred while processing the SOAP request.</faultstring></s:Fault></s:Body></s:Envelope>",
                    xml);
            }

            RecordedLogEntry serError = Assert.Single(
                harness.LoggerProvider.Entries,
                static e => e.LogLevel == LogLevel.Error && e.EventId.Id == 1008);
            Assert.NotNull(serError.Exception);
            Assert.Equal("SECRET_SERIALIZE_FAILURE_TOKEN", serError.Exception.Message);
            Assert.Contains("FailureStage=response_serialization", serError.FormattedMessage, StringComparison.Ordinal);
            Assert.DoesNotContain("SENSITIVE_BODY_SECRET", serError.FormattedMessage, StringComparison.Ordinal);

            byte[] invalidDetailReq = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><Echo xmlns=\"urn:echo\"><Message>__THROW_INVALID_DETAIL__</Message></Echo></s:Body></s:Envelope>"u8.ToArray();
            harness.LoggerProvider.Clear();

            using (HttpRequestMessage req = CreateSoap11Request("/logging-faults.svc", "urn:echo/IEchoService/Echo", invalidDetailReq))
            using (HttpResponseMessage res = await harness.Client.SendAsync(req, TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);
                Assert.Equal(string.Empty, await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            }

            RecordedLogEntry stagingError = Assert.Single(
                harness.LoggerProvider.Entries,
                static e => e.LogLevel == LogLevel.Error && e.EventId.Id == 1008);
            Assert.NotNull(stagingError.Exception);
            Assert.Contains("FailureStage=fault_staging", stagingError.FormattedMessage, StringComparison.Ordinal);
        }

        [Fact]
        public async Task StructuredFaultDetail_Soap11AndSoap12_ReturnsExactDetailAndStatusOverKestrel()
        {
            await using IntegrationHarness harness = await IntegrationHarness.StartAsync();

            byte[] soap11Req = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><Echo xmlns=\"urn:echo\"><Message>__THROW_FAULT__</Message></Echo></s:Body></s:Envelope>"u8.ToArray();
            using (HttpRequestMessage req = CreateSoap11Request("/logging-faults.svc", "urn:echo/IEchoService/Echo", soap11Req))
            using (HttpResponseMessage res = await harness.Client.SendAsync(req, TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault><faultcode>s:Client</faultcode><faultstring>Typed fault from endpoint.</faultstring><detail><IntegrationFaultDetail xmlns=\"urn:faults\"><Code>E_INTEGRATION</Code><Message>Rejected by business rule.</Message></IntegrationFaultDetail></detail></s:Fault></s:Body></s:Envelope>",
                    await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            }

            byte[] soap12Req = "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body><Echo xmlns=\"urn:echo\"><Message>__THROW_FAULT__</Message></Echo></s:Body></s:Envelope>"u8.ToArray();
            using (HttpRequestMessage req12 = CreateSoap12Request("/logging-faults.svc", "urn:echo/IEchoService/Echo", soap12Req))
            using (HttpResponseMessage res12 = await harness.Client.SendAsync(req12, TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.BadRequest, res12.StatusCode);
                Assert.Equal(
                    "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body><s:Fault><s:Code><s:Value>s:Sender</s:Value></s:Code><s:Reason><s:Text xml:lang=\"en-US\">Typed fault from endpoint.</s:Text></s:Reason><s:Detail><IntegrationFaultDetail xmlns=\"urn:faults\"><Code>E_INTEGRATION</Code><Message>Rejected by business rule.</Message></IntegrationFaultDetail></s:Detail></s:Fault></s:Body></s:Envelope>",
                    await res12.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            }
        }

        [Fact]
        public async Task VaryAcceptEncoding_PreservesExistingVaryAndEmitsOnDirectStreamingWhenAcceptEncodingOmitted()
        {
            await using IntegrationHarness harness = await IntegrationHarness.StartAsync();

            byte[] echoReq = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><Echo xmlns=\"urn:echo\"><Message>vary-check</Message></Echo></s:Body></s:Envelope>"u8.ToArray();
            const string expectedResponse = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><s:Body><EchoResponse xmlns=\"urn:echo\"><EchoResult>vary-check</EchoResult></EchoResponse></s:Body></s:Envelope>";

            using (HttpRequestMessage req = CreateSoap11Request("/vary-preserved.svc", "urn:echo/IEchoService/Echo", echoReq))
            using (HttpResponseMessage res = await harness.Client.SendAsync(req, TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, res.StatusCode);
                Assert.Contains("Origin", res.Headers.Vary, StringComparer.OrdinalIgnoreCase);
                Assert.Contains("Accept-Encoding", res.Headers.Vary, StringComparer.OrdinalIgnoreCase);
                Assert.Equal(expectedResponse, await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            }

            using (HttpRequestMessage req = CreateSoap11Request("/direct-streaming-compressed.svc", "urn:echo/IEchoService/Echo", echoReq))
            using (HttpResponseMessage res = await harness.Client.SendAsync(req, TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, res.StatusCode);
                Assert.Empty(res.Content.Headers.ContentEncoding);
                Assert.Contains("Accept-Encoding", res.Headers.Vary, StringComparer.OrdinalIgnoreCase);
                Assert.Equal(expectedResponse, await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            }
        }

        #endregion

        #region Private Methods

        private static byte[] CompressBytes(byte[] payload, SoapCompressionEncoding encoding)
        {
            using BoundedPooledBufferWriter writer = new BoundedPooledBufferWriter(256, 65536);
            SoapCompressionCodec.Compress(payload, encoding, writer);

            return writer.WrittenSpan.ToArray();
        }

        private static HttpRequestMessage CreateCompressedSoap11Request(
            string path,
            string action,
            byte[] compressedPayload,
            string contentEncoding)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, path);
            request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{action}\"");
            request.Content = new ByteArrayContent(compressedPayload);
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("text/xml; charset=utf-8");
            request.Content.Headers.TryAddWithoutValidation("Content-Encoding", contentEncoding);

            return request;
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

        private static byte[] DecompressBytes(byte[] compressedWire, SoapCompressionEncoding encoding)
        {
            using SoapRequestBuffer buffer = SoapCompressionCodec.Decompress(compressedWire, encoding, maxDecompressedBytes: 256 * 1024);

            return buffer.Span.ToArray();
        }

        #endregion

        #region Nested Types

        private sealed class FailingHeaderHandler : ISoapHeaderHandler
        {

            #region Properties

            public SoapQName HeaderName { get; } = new SoapQName("urn:faults", "FailHeader");

            #endregion

            #region Public Methods

            public ValueTask HandleAsync(
                SoapHeaderValue header,
                SoapCallContext context,
                CancellationToken cancellationToken)
            {
                throw new InvalidOperationException("HEADER_DB_TOKEN");
            }

            #endregion

        }

        private sealed class IntegrationHarness : IAsyncDisposable
        {

            #region Fields

            private int _serviceInvocations;

            #endregion

            #region Properties

            public HttpClient Client { get; }

            public RecordingLoggerProvider LoggerProvider { get; }

            public int ServiceInvocations => Volatile.Read(ref _serviceInvocations);

            private WebApplication App { get; }

            #endregion

            #region Constructors

            private IntegrationHarness(
                WebApplication app,
                HttpClient client,
                RecordingLoggerProvider loggerProvider)
            {
                App = app;
                Client = client;
                LoggerProvider = loggerProvider;
            }

            #endregion

            #region Public Methods

            public async ValueTask DisposeAsync()
            {
                Client.Dispose();
                await App.StopAsync().ConfigureAwait(false);
                await App.DisposeAsync().ConfigureAwait(false);
            }

            public static async Task<IntegrationHarness> StartAsync()
            {
                WebApplicationBuilder builder = WebApplication.CreateBuilder();
                builder.WebHost.UseUrls("http://127.0.0.1:0");

                RecordingLoggerProvider loggerProvider = new RecordingLoggerProvider();
                builder.Logging.ClearProviders();
                builder.Logging.AddProvider(loggerProvider);
                builder.Logging.SetMinimumLevel(LogLevel.Information);

                builder.Services.AddFastSoap(static options =>
                {
                    options.EnabledVersions = SoapVersionFlags.Both;
                });

                WebApplication app = builder.Build();
                ExtendedIntegrationXmlContext xmlContext = new ExtendedIntegrationXmlContext(new XmlSerializerOptions
                {
                    AllowDuplicateProperties = false,
                    OmitXmlDeclaration = true,
                    UnmappedMemberHandling = XmlUnmappedMemberHandling.Disallow
                });

                IntegrationHarness? holder = null;

                void Increment()
                {
                    if (holder is not null)
                    {
                        Interlocked.Increment(ref holder._serviceInvocations);
                    }
                }

                app.MapSoap("/default.svc", soap =>
                {
                    soap.MapOperation(
                        EchoDescriptor,
                        xmlContext.EchoRequest,
                        xmlContext.EchoResponse,
                        (req, _, _) =>
                        {
                            Increment();

                            return ValueTask.FromResult(new EchoResponse { EchoResult = req.Message });
                        });
                });

                app.MapSoap("/compressed.svc", soap =>
                {
                    soap.ConfigureOptions(static options =>
                    {
                        options.AllowCompressedRequests = true;
                    });
                    soap.MapOperation(
                        EchoDescriptor,
                        xmlContext.EchoRequest,
                        xmlContext.EchoResponse,
                        (req, _, _) =>
                        {
                            Increment();

                            return ValueTask.FromResult(new EchoResponse { EchoResult = req.Message });
                        });
                });

                app.MapSoap("/compressed-quota.svc", soap =>
                {
                    soap.ConfigureOptions(static options =>
                    {
                        options.AllowCompressedRequests = true;
                        options.MaxRequestBytes = 4096;
                    });
                    soap.MapOperation(
                        EchoDescriptor,
                        xmlContext.EchoRequest,
                        xmlContext.EchoResponse,
                        (req, _, _) =>
                        {
                            Increment();

                            return ValueTask.FromResult(new EchoResponse { EchoResult = req.Message });
                        });
                });

                app.MapSoap("/compressed-non-pow2-quota.svc", soap =>
                {
                    soap.ConfigureOptions(static options =>
                    {
                        options.AllowCompressedRequests = true;
                        options.MaxRequestBytes = 5000;
                    });
                    soap.MapOperation(
                        EchoDescriptor,
                        xmlContext.EchoRequest,
                        xmlContext.EchoResponse,
                        (req, _, _) =>
                        {
                            Increment();

                            return ValueTask.FromResult(new EchoResponse { EchoResult = req.Message });
                        });
                });

                app.MapSoap("/tight-compressed-response.svc", soap =>
                {
                    soap.ConfigureOptions(static options =>
                    {
                        options.EnableResponseCompression = true;
                        options.ResponseCompressionMinBytes = 512;
                        options.MaxResponseBytes = 8800;
                    });
                    soap.MapOperation(
                        LargeBatchDescriptor,
                        xmlContext.LargeBatchRequestWire,
                        xmlContext.LargeBatchResponseWire,
                        static (req, _, _) => ValueTask.FromResult(new LargeBatchResponseWire { Items = [.. req.Payload.Split('|')] }));
                });

                app.Use(static async (context, next) =>
                {
                    if (context.Request.Path.StartsWithSegments("/vary-preserved.svc", StringComparison.Ordinal))
                    {
                        context.Response.Headers.Vary = "Origin";
                    }

                    await next().ConfigureAwait(false);
                });

                app.MapSoap("/vary-preserved.svc", soap =>
                {
                    soap.ConfigureOptions(static options =>
                    {
                        options.EnableResponseCompression = true;
                        options.ResponseCompressionMinBytes = 512;
                    });
                    soap.MapOperation(
                        EchoDescriptor,
                        xmlContext.EchoRequest,
                        xmlContext.EchoResponse,
                        static (req, _, _) => ValueTask.FromResult(new EchoResponse { EchoResult = req.Message }));
                });

                app.MapSoap("/direct-streaming-compressed.svc", soap =>
                {
                    soap.ConfigureOptions(static options =>
                    {
                        options.EnableDirectResponseStreaming = true;
                        options.EnableResponseCompression = true;
                        options.ResponseCompressionMinBytes = 512;
                    });
                    soap.MapOperation(
                        EchoDescriptor,
                        xmlContext.EchoRequest,
                        xmlContext.EchoResponse,
                        static (req, _, _) => ValueTask.FromResult(new EchoResponse { EchoResult = req.Message }));
                });

                app.MapSoap("/compressed-response.svc", soap =>
                {
                    soap.ConfigureOptions(static options =>
                    {
                        options.EnableResponseCompression = true;
                        options.ResponseCompressionMinBytes = 512;
                    });
                    soap.MapOperation(
                        EchoDescriptor,
                        xmlContext.EchoRequest,
                        xmlContext.EchoResponse,
                        static (req, _, _) => ValueTask.FromResult(new EchoResponse { EchoResult = req.Message }));
                    soap.MapOperation(
                        LargeBatchDescriptor,
                        xmlContext.LargeBatchRequestWire,
                        xmlContext.LargeBatchResponseWire,
                        static (req, _, _) =>
                        {
                            List<string> items = new List<string>(60);
                            for (int index = 0; index < 60; index++)
                            {
                                items.Add($"{req.Payload}-{index}-REPEATED_SEGMENT");
                            }

                            return ValueTask.FromResult(new LargeBatchResponseWire { Items = items });
                        });
                });

                app.MapSoap("/direct-streaming.svc", soap =>
                {
                    soap.ConfigureOptions(static options =>
                    {
                        options.EnableDirectResponseStreaming = true;
                    });
                    soap.MapOperation(
                        EchoDescriptor,
                        xmlContext.EchoRequest,
                        xmlContext.EchoResponse,
                        (req, _, _) =>
                        {
                            if (string.Equals(req.Message, "__THROW_FAULT__", StringComparison.Ordinal))
                            {
                                throw SoapFaultDetailSerializer.CreateFaultException(
                                    SoapFaultKind.Sender,
                                    "Handled fault before streaming.",
                                    new IntegrationFaultDetailWire
                                    {
                                        Code = "PRE_START",
                                        Message = "Rejected before start."
                                    },
                                    xmlContext.IntegrationFaultDetailWire);
                            }

                            return ValueTask.FromResult(new EchoResponse { EchoResult = req.Message });
                        });
                    soap.MapOperation(
                        ThrowInvalidDataSerializeDescriptor,
                        xmlContext.ThrowInvalidDataSerializeRequestWire,
                        xmlContext.ThrowInvalidDataOnSerializeResponseWire,
                        static (_, _, _) => ValueTask.FromResult(new ThrowInvalidDataOnSerializeResponseWire()));
                    soap.MapOperation(
                        ThrowSerializeDescriptor,
                        xmlContext.ThrowSerializeRequestWire,
                        xmlContext.ThrowOnSerializeResponseWire,
                        static (_, _, _) => ValueTask.FromResult(new ThrowOnSerializeResponseWire()));
                });

                app.MapSoap("/logging-faults.svc", soap =>
                {
                    soap.AddHeaderHandler(new FailingHeaderHandler());
                    soap.MapOperation(
                        EchoDescriptor,
                        xmlContext.EchoRequest,
                        xmlContext.EchoResponse,
                        (req, _, _) =>
                        {
                            if (string.Equals(req.Message, "__THROW_INTERNAL__", StringComparison.Ordinal))
                            {
                                throw new InvalidOperationException("HANDLER_SECRET_KEY");
                            }

                            if (string.Equals(req.Message, "__THROW_INVALID_DETAIL__", StringComparison.Ordinal))
                            {
                                throw new SoapFaultException(
                                    SoapFaultKind.Sender,
                                    "Malformed detail test.",
                                    "<Broken><Unclosed>"u8.ToArray());
                            }

                            if (string.Equals(req.Message, "__THROW_FAULT__", StringComparison.Ordinal))
                            {
                                throw SoapFaultDetailSerializer.CreateFaultException(
                                    SoapFaultKind.Sender,
                                    "Typed fault from endpoint.",
                                    new IntegrationFaultDetailWire
                                    {
                                        Code = "E_INTEGRATION",
                                        Message = "Rejected by business rule."
                                    },
                                    xmlContext.IntegrationFaultDetailWire);
                            }

                            return ValueTask.FromResult(new EchoResponse { EchoResult = req.Message });
                        });
                    soap.MapOperation(
                        ThrowSerializeDescriptor,
                        xmlContext.ThrowSerializeRequestWire,
                        xmlContext.ThrowOnSerializeResponseWire,
                        static (_, _, _) => ValueTask.FromResult(new ThrowOnSerializeResponseWire()));
                });

                await app.StartAsync().ConfigureAwait(false);

                IServer server = app.Services.GetRequiredService<IServer>();
                IServerAddressesFeature addresses = server.Features.Get<IServerAddressesFeature>()!;
                Uri baseUri = new Uri(addresses.Addresses.First());

                holder = new IntegrationHarness(app, new HttpClient { BaseAddress = baseUri }, loggerProvider);

                return holder;
            }

            #endregion

        }

        private sealed record RecordedLogEntry(
            string Category,
            LogLevel LogLevel,
            EventId EventId,
            string FormattedMessage,
            Exception? Exception);

        private sealed class RecordingLoggerProvider : ILoggerProvider
        {

            #region Fields

            private readonly ConcurrentQueue<RecordedLogEntry> _entries = new ConcurrentQueue<RecordedLogEntry>();

            #endregion

            #region Properties

            public IReadOnlyList<RecordedLogEntry> Entries => [.. _entries];

            #endregion

            #region Public Methods

            public void Clear()
            {
                _entries.Clear();
            }

            public ILogger CreateLogger(string categoryName)
            {
                return new RecordingLogger(categoryName, _entries);
            }

            public void Dispose()
            {
            }

            #endregion

            private sealed class RecordingLogger : ILogger
            {

                #region Fields

                private readonly string _categoryName;
                private readonly ConcurrentQueue<RecordedLogEntry> _entries;

                #endregion

                #region Constructors

                public RecordingLogger(string categoryName, ConcurrentQueue<RecordedLogEntry> entries)
                {
                    _categoryName = categoryName;
                    _entries = entries;
                }

                #endregion

                #region Public Methods

                public IDisposable? BeginScope<TState>(TState state) where TState : notnull
                {
                    return null;
                }

                public bool IsEnabled(LogLevel logLevel)
                {
                    return true;
                }

                public void Log<TState>(
                    LogLevel logLevel,
                    EventId eventId,
                    TState state,
                    Exception? exception,
                    Func<TState, Exception?, string> formatter)
                {
                    _entries.Enqueue(new RecordedLogEntry(
                        _categoryName,
                        logLevel,
                        eventId,
                        formatter(state, exception),
                        exception));
                }

                #endregion

            }

        }

        #endregion

    }

}
