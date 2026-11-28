using System;
using System.IO;
using System.IO.Compression;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Tasks;

using CloudNimble.Text.Xml.Serialization;

using FastSoap.Abstractions;
using FastSoap.Core.Buffers;
using FastSoap.Core.Serialization;

using Xunit;

namespace FastSoap.SerializationTests
{

    [XmlRoot(ElementName = "SampleFaultDetail", Namespace = "urn:faults")]
    public sealed class SampleFaultDetailWire
    {

        #region Properties

        [XmlElement(ElementName = "Code", Namespace = "urn:faults", Order = 0)]
        public string Code { get; set; } = string.Empty;

        [XmlElement(ElementName = "Detail", Namespace = "urn:faults", IsNullable = true, Order = 1)]
        public string? Detail { get; set; }

        #endregion

    }

    [XmlRoot(ElementName = "SparseCustomerResponse", Namespace = "urn:sparse")]
    public sealed class SparseCustomerResponseWire
    {

        #region Properties

        [XmlElement(ElementName = "AddressLine1", Namespace = "urn:sparse", IsNullable = true, Order = 1)]
        public string? AddressLine1 { get; set; }

        [XmlElement(ElementName = "AddressLine2", Namespace = "urn:sparse", IsNullable = true, Order = 2)]
        public string? AddressLine2 { get; set; }

        [XmlElement(ElementName = "City", Namespace = "urn:sparse", IsNullable = true, Order = 3)]
        public string? City { get; set; }

        [XmlElement(ElementName = "CustomerId", Namespace = "urn:sparse", Order = 0)]
        public string CustomerId { get; set; } = string.Empty;

        [XmlElement(ElementName = "PostalCode", Namespace = "urn:sparse", IsNullable = true, Order = 4)]
        public string? PostalCode { get; set; }

        [XmlElement(ElementName = "SecondaryEmail", Namespace = "urn:sparse", IsNullable = true, Order = 5)]
        public string? SecondaryEmail { get; set; }

        [XmlElement(ElementName = "SecondaryPhone", Namespace = "urn:sparse", IsNullable = true, Order = 6)]
        public string? SecondaryPhone { get; set; }

        #endregion

    }

    [XmlSerializable<SampleFaultDetailWire>]
    [XmlSerializable<SparseCustomerResponseWire>]
    public partial class SparseAndFaultXmlContext : XmlSerializerContext
    {
    }

    /// <summary>
    /// Verifies single root <c>xmlns:xsi</c> declaration optimization on <see cref="SoapResponseWriter"/>,
    /// reflection-free <see cref="SoapFaultDetailSerializer"/>, and pooled <see cref="SoapCompressionCodec"/> behavior.
    /// </summary>
    public sealed class SparseXsiAndFaultDetailTests
    {

        #region Fields

        private static readonly SparseAndFaultXmlContext s_context = new SparseAndFaultXmlContext(
            XmlEssentialsAdapter.CreateStrictSerializerOptions());

        #endregion

        #region Public Methods

        [Fact]
        public void SoapCompressionCodec_CompressBrotli_RemainingBudgetHintAllowsExactCompressedMaxBytes()
        {
            TrackingArrayPool pool = new TrackingArrayPool(minimumBucketSize: 64);
            byte[] payload = Encoding.UTF8.GetBytes(new string('B', 4096));

            int exactCompressedLength;
            using (BoundedPooledBufferWriter probe = new BoundedPooledBufferWriter(256, 8192, clearOnReturn: true, pool))
            {
                SoapCompressionCodec.Compress(payload, SoapCompressionEncoding.Brotli, probe);
                exactCompressedLength = probe.WrittenCount;
            }

            using (BoundedPooledBufferWriter tight = new BoundedPooledBufferWriter(exactCompressedLength, exactCompressedLength, clearOnReturn: true, pool))
            {
                SoapCompressionCodec.Compress(payload, SoapCompressionEncoding.Brotli, tight);
                Assert.Equal(exactCompressedLength, tight.WrittenCount);
            }

            Assert.Equal(0, pool.ActiveRentalCount);
            Assert.Equal(pool.TotalRentCalls, pool.TotalReturnCalls);
        }

        [Theory]
        [InlineData("br", SoapCompressionEncoding.Brotli)]
        [InlineData("gzip", SoapCompressionEncoding.Gzip)]
        [InlineData("x-gzip", SoapCompressionEncoding.Gzip)]
        [InlineData("x-gzip;q=0.9, deflate;q=0.5", SoapCompressionEncoding.Gzip)]
        [InlineData("gzip;q=0, x-gzip;q=0.8", SoapCompressionEncoding.Gzip)]
        [InlineData("deflate", SoapCompressionEncoding.Deflate)]
        [InlineData("deflate, gzip, br", SoapCompressionEncoding.Brotli)]
        [InlineData("gzip;q=0.9, br;q=0.5", SoapCompressionEncoding.Gzip)]
        [InlineData("br;q=0, gzip;q=0.8, deflate;q=0.9", SoapCompressionEncoding.Deflate)]
        [InlineData("br;q=0, *", SoapCompressionEncoding.Gzip)]
        [InlineData("identity;q=0.9, gzip;q=0.5", SoapCompressionEncoding.Identity)]
        [InlineData("identity;q=0.8, br;q=0.5, gzip;q=0.7", SoapCompressionEncoding.Identity)]
        [InlineData("identity;q=0.8, *;q=0.5", SoapCompressionEncoding.Identity)]
        [InlineData("identity;q=0.5, gzip;q=0.5", SoapCompressionEncoding.Gzip)]
        [InlineData("identity;q=0.5, br;q=0.8, gzip;q=0.3", SoapCompressionEncoding.Brotli)]
        [InlineData("identity", SoapCompressionEncoding.Identity)]
        [InlineData("", SoapCompressionEncoding.Identity)]
        public void SoapCompressionCodec_NegotiateAcceptEncoding_HonorsPreferenceAndQualityWeights(
            string acceptEncoding,
            SoapCompressionEncoding expected)
        {
            Assert.Equal(expected, SoapCompressionCodec.NegotiateAcceptEncoding(acceptEncoding.AsSpan()));
        }

        [Fact]
        public void SoapCompressionCodec_NonPowerOfTwoQuota_RejectsOverflowAllowsExactAndRejectsTrailingBrotliGarbage()
        {
            const int maxDecompressedBytes = 5000;
            TrackingArrayPool pool = new TrackingArrayPool(minimumBucketSize: 64);
            byte[] exactPayload = Encoding.UTF8.GetBytes(new string('E', maxDecompressedBytes));
            byte[] oversizedPayload = Encoding.UTF8.GetBytes(new string('O', 6000));

            foreach (SoapCompressionEncoding encoding in new[]
            {
                SoapCompressionEncoding.Brotli,
                SoapCompressionEncoding.Gzip,
                SoapCompressionEncoding.Deflate
            })
            {
                using BoundedPooledBufferWriter exactCompressed = new BoundedPooledBufferWriter(256, 16384, clearOnReturn: true, pool);
                SoapCompressionCodec.Compress(exactPayload, encoding, exactCompressed);

                using (SoapRequestBuffer exactResult = SoapCompressionCodec.Decompress(
                    exactCompressed.WrittenMemory,
                    encoding,
                    maxDecompressedBytes,
                    clearOnReturn: true,
                    pool: pool))
                {
                    Assert.Equal(maxDecompressedBytes, exactResult.Length);
                    Assert.True(exactResult.Span.SequenceEqual(exactPayload));
                }

                if (encoding == SoapCompressionEncoding.Brotli)
                {
                    byte[] exactWithTrailingGarbage = new byte[exactCompressed.WrittenCount + 2];
                    exactCompressed.WrittenSpan.CopyTo(exactWithTrailingGarbage);
                    exactWithTrailingGarbage[^2] = 0x00;
                    exactWithTrailingGarbage[^1] = 0xFF;

                    Assert.Throws<InvalidDataException>(() =>
                        SoapCompressionCodec.Decompress(
                            exactWithTrailingGarbage,
                            SoapCompressionEncoding.Brotli,
                            maxDecompressedBytes,
                            clearOnReturn: true,
                            pool: pool));
                }

                using BoundedPooledBufferWriter oversizedCompressed = new BoundedPooledBufferWriter(256, 16384, clearOnReturn: true, pool);
                SoapCompressionCodec.Compress(oversizedPayload, encoding, oversizedCompressed);

                RequestQuotaExceededException overflowEx = Assert.Throws<RequestQuotaExceededException>(() =>
                    SoapCompressionCodec.Decompress(
                        oversizedCompressed.WrittenMemory,
                        encoding,
                        maxDecompressedBytes,
                        clearOnReturn: true,
                        pool: pool));

                Assert.Equal(maxDecompressedBytes, overflowEx.MaxBytes);
            }

            Assert.Equal(0, pool.ActiveRentalCount);
            Assert.Equal(pool.TotalRentCalls, pool.TotalReturnCalls);
        }

        [Fact]
        public async Task SoapCompressionCodec_RoundTripsAndEnforcesDecompressionBombQuotaWithZeroPoolLeaks()
        {
            TrackingArrayPool pool = new TrackingArrayPool(minimumBucketSize: 64);
            byte[] payload = Encoding.UTF8.GetBytes(new string('A', 8192));

            foreach (SoapCompressionEncoding encoding in new[]
            {
                SoapCompressionEncoding.Brotli,
                SoapCompressionEncoding.Gzip,
                SoapCompressionEncoding.Deflate
            })
            {
                using BoundedPooledBufferWriter compressed = new BoundedPooledBufferWriter(256, 16384, clearOnReturn: true, pool);
                SoapCompressionCodec.Compress(payload, encoding, compressed);

                Assert.True(compressed.WrittenCount < 512);

                Pipe pipe = new Pipe();
                await pipe.Writer.WriteAsync(compressed.WrittenMemory, TestContext.Current.CancellationToken);
                await pipe.Writer.CompleteAsync();

                using (SoapRequestBuffer decompressed = await SoapRequestBuffer.CopyFromPipeAsync(
                    pipe.Reader,
                    maxBytes: 8192,
                    encoding,
                    initialCapacity: 64,
                    clearOnReturn: true,
                    pool: pool,
                    cancellationToken: TestContext.Current.CancellationToken))
                {
                    Assert.Equal(8192, decompressed.Length);
                    Assert.True(decompressed.Span.SequenceEqual(payload));
                }

                RequestQuotaExceededException bombEx = Assert.Throws<RequestQuotaExceededException>(() =>
                    SoapCompressionCodec.Decompress(
                        compressed.WrittenMemory,
                        encoding,
                        maxDecompressedBytes: 4096,
                        clearOnReturn: true,
                        pool: pool));

                Assert.Equal(4096, bombEx.MaxBytes);

                Assert.Throws<InvalidDataException>(() =>
                    SoapCompressionCodec.Decompress(
                        "not-valid-compressed-bytes"u8.ToArray(),
                        encoding,
                        maxDecompressedBytes: 4096,
                        clearOnReturn: true,
                        pool: pool));
            }

            Assert.Equal(0, pool.ActiveRentalCount);
            Assert.Equal(pool.TotalRentCalls, pool.TotalReturnCalls);
        }

        [Fact]
        public void SoapFaultDetailSerializer_SerializesTypedDtoAndWcfExceptionDetailWithPoolCleanup()
        {
            TrackingArrayPool pool = new TrackingArrayPool(minimumBucketSize: 64);

            SoapFaultException typedEx = SoapFaultDetailSerializer.CreateFaultException(
                SoapFaultKind.Sender,
                "Invalid item.",
                new SampleFaultDetailWire { Code = "E_NIL", Detail = null },
                s_context.SampleFaultDetailWire,
                pool: pool);

            Assert.Equal(
                "<SampleFaultDetail xmlns=\"urn:faults\"><Code>E_NIL</Code><Detail xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:nil=\"true\"/></SampleFaultDetail>",
                Encoding.UTF8.GetString(typedEx.DetailXmlUtf8.Span));

            SoapExceptionDetail exceptionDetail = SoapExceptionDetail.FromException(
                new InvalidOperationException("Outer error", new ArgumentException("Inner error")),
                includeStackTrace: false);

            SoapFaultException wcfEx = SoapFaultDetailSerializer.CreateFaultException(
                SoapFaultKind.Receiver,
                "Server fault.",
                exceptionDetail,
                pool: pool);

            Assert.Equal(
                "<ExceptionDetail xmlns=\"http://schemas.datacontract.org/2004/07/System.ServiceModel\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><HelpLink xsi:nil=\"true\"/><InnerException><HelpLink xsi:nil=\"true\"/><InnerException xsi:nil=\"true\"/><Message>Inner error</Message><StackTrace xsi:nil=\"true\"/><Type>System.ArgumentException</Type></InnerException><Message>Outer error</Message><StackTrace xsi:nil=\"true\"/><Type>System.InvalidOperationException</Type></ExceptionDetail>",
                Encoding.UTF8.GetString(wcfEx.DetailXmlUtf8.Span));

            Assert.Throws<ResponseQuotaExceededException>(() =>
                SoapFaultDetailSerializer.Serialize(
                    new SampleFaultDetailWire { Code = "TOO_BIG_FOR_LIMIT", Detail = "Payload" },
                    s_context.SampleFaultDetailWire,
                    maxBytes: 16,
                    pool: pool));

            Assert.Equal(0, pool.ActiveRentalCount);
            Assert.Equal(pool.TotalRentCalls, pool.TotalReturnCalls);
        }

        [Fact]
        public void SoapResponseWriter_SparseDtoWithSixNullElements_DeclaresXmlnsXsiOnceOnEnvelope()
        {
            SparseCustomerResponseWire sparse = new SparseCustomerResponseWire
            {
                CustomerId = "CUST-42",
                AddressLine1 = null,
                AddressLine2 = null,
                City = null,
                PostalCode = null,
                SecondaryEmail = null,
                SecondaryPhone = null
            };

            using BoundedPooledBufferWriter writer = new BoundedPooledBufferWriter(256, 4096);
            long writtenBytes = SoapResponseWriter.WriteResponseAndCount(
                SoapVersion.Soap11,
                writer,
                sparse,
                s_context.SparseCustomerResponseWire);

            string xml = Encoding.UTF8.GetString(writer.WrittenSpan);
            const string expectedXml =
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                "<s:Body>" +
                "<SparseCustomerResponse xmlns=\"urn:sparse\">" +
                "<CustomerId>CUST-42</CustomerId>" +
                "<AddressLine1 xsi:nil=\"true\"/>" +
                "<AddressLine2 xsi:nil=\"true\"/>" +
                "<City xsi:nil=\"true\"/>" +
                "<PostalCode xsi:nil=\"true\"/>" +
                "<SecondaryEmail xsi:nil=\"true\"/>" +
                "<SecondaryPhone xsi:nil=\"true\"/>" +
                "</SparseCustomerResponse>" +
                "</s:Body>" +
                "</s:Envelope>";

            Assert.Equal(expectedXml, xml);
            Assert.Equal(expectedXml.Length, writtenBytes);
            Assert.Equal(expectedXml.Length, writer.WrittenCount);

            int firstXsiIndex = xml.IndexOf("xmlns:xsi=", StringComparison.Ordinal);
            int secondXsiIndex = xml.IndexOf("xmlns:xsi=", firstXsiIndex + 1, StringComparison.Ordinal);
            Assert.Equal(64, firstXsiIndex);
            Assert.Equal(-1, secondXsiIndex);
        }

        #endregion

    }

}
