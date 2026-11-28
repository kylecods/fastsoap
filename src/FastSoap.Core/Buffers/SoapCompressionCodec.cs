using System;
using System.Buffers;
using System.Globalization;
using System.IO;
using System.IO.Compression;

using FastSoap.Core.Protocol;

namespace FastSoap.Core.Buffers
{

    /// <summary>
    /// Identifies supported HTTP transport compression encodings for SOAP requests and responses.
    /// </summary>
    public enum SoapCompressionEncoding
    {
        /// <summary>
        /// Uncompressed identity encoding.
        /// </summary>
        Identity = 0,

        /// <summary>
        /// HTTP <c>gzip</c> compression.
        /// </summary>
        Gzip = 1,

        /// <summary>
        /// HTTP <c>br</c> (Brotli) compression.
        /// </summary>
        Brotli = 2,

        /// <summary>
        /// HTTP <c>deflate</c> compression.
        /// </summary>
        Deflate = 3
    }

    /// <summary>
    /// Provides bounded, pool-backed HTTP compression and decompression helpers for
    /// <c>br</c>, <c>gzip</c>, and <c>deflate</c> SOAP payloads.
    /// </summary>
    /// <remarks>
    /// Decompression enforces <c>maxDecompressedBytes</c> incrementally using a 1-byte overflow probe
    /// so decompression bombs fail immediately with <see cref="RequestQuotaExceededException"/> without
    /// buffering unbounded decompressed output.
    /// </remarks>
    public static class SoapCompressionCodec
    {

        #region Public Methods

        /// <summary>
        /// Compresses <paramref name="uncompressedBytes"/> using <paramref name="encoding"/> directly into
        /// <paramref name="destination"/>.
        /// </summary>
        /// <param name="uncompressedBytes">The staged uncompressed UTF-8 payload bytes.</param>
        /// <param name="encoding">The negotiated target compression encoding.</param>
        /// <param name="destination">The bounded pooled buffer writer receiving compressed bytes.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="destination"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="encoding"/> is undefined or <see cref="SoapCompressionEncoding.Identity"/>.</exception>
        public static void Compress(
            ReadOnlySpan<byte> uncompressedBytes,
            SoapCompressionEncoding encoding,
            BoundedPooledBufferWriter destination)
        {
            ArgumentNullException.ThrowIfNull(destination);

            if (encoding == SoapCompressionEncoding.Brotli)
            {
                CompressBrotli(uncompressedBytes, destination);

                return;
            }

            if (encoding is not (SoapCompressionEncoding.Gzip or SoapCompressionEncoding.Deflate))
            {
                throw new ArgumentOutOfRangeException(nameof(encoding), encoding, "Unsupported response compression encoding.");
            }

            using BufferWriterStream output = new BufferWriterStream(destination);
            using Stream compressor = encoding == SoapCompressionEncoding.Gzip
                ? new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true)
                : new DeflateStream(output, CompressionLevel.Fastest, leaveOpen: true);

            compressor.Write(uncompressedBytes);
        }

        /// <summary>
        /// Decompresses <paramref name="compressedBytes"/> into a bounded pooled <see cref="SoapRequestBuffer"/>,
        /// enforcing <paramref name="maxDecompressedBytes"/> on the decompressed output.
        /// </summary>
        /// <param name="compressedBytes">The bounded compressed request wire bytes.</param>
        /// <param name="encoding">The validated <see cref="SoapCompressionEncoding"/>.</param>
        /// <param name="maxDecompressedBytes">Maximum permitted decompressed payload size in bytes.</param>
        /// <param name="clearOnReturn">Whether to zero-clear rented buffers before returning to <paramref name="pool"/>.</param>
        /// <param name="pool">Optional <see cref="ArrayPool{T}"/>; defaults to <see cref="ArrayPool{T}.Shared"/>.</param>
        /// <returns>An owned <see cref="SoapRequestBuffer"/> containing the decompressed UTF-8 XML bytes.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxDecompressedBytes"/> is non-positive or <paramref name="encoding"/> is undefined.</exception>
        /// <exception cref="RequestQuotaExceededException">Thrown when decompressed output exceeds <paramref name="maxDecompressedBytes"/>.</exception>
        /// <exception cref="InvalidDataException">Thrown when <paramref name="compressedBytes"/> is empty or malformed for <paramref name="encoding"/>.</exception>
        public static SoapRequestBuffer Decompress(
            ReadOnlyMemory<byte> compressedBytes,
            SoapCompressionEncoding encoding,
            int maxDecompressedBytes,
            bool clearOnReturn = true,
            ArrayPool<byte>? pool = null)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxDecompressedBytes, 0);

            if (!Enum.IsDefined(encoding))
            {
                throw new ArgumentOutOfRangeException(nameof(encoding), encoding, "Unsupported compression encoding.");
            }

            ArrayPool<byte> effectivePool = pool ?? ArrayPool<byte>.Shared;

            if (encoding == SoapCompressionEncoding.Identity)
            {
                return CopyIdentityToBuffer(compressedBytes.Span, maxDecompressedBytes, clearOnReturn, effectivePool);
            }

            if (compressedBytes.IsEmpty)
            {
                throw new InvalidDataException("Compressed request payload cannot be empty.");
            }

            if (encoding == SoapCompressionEncoding.Brotli)
            {
                return DecompressBrotli(compressedBytes.Span, maxDecompressedBytes, clearOnReturn, effectivePool);
            }

            return DecompressStream(compressedBytes, encoding, maxDecompressedBytes, clearOnReturn, effectivePool);
        }

        /// <summary>
        /// Negotiates the preferred supported response encoding from an HTTP <c>Accept-Encoding</c> header value,
        /// honoring quality weights (<c>q</c>) and <c>br &gt; gzip &gt; deflate</c> preference order.
        /// </summary>
        /// <param name="acceptEncoding">The raw <c>Accept-Encoding</c> header value.</param>
        /// <returns>The negotiated <see cref="SoapCompressionEncoding"/>, or <see cref="SoapCompressionEncoding.Identity"/> when none is acceptable.</returns>
        public static SoapCompressionEncoding NegotiateAcceptEncoding(ReadOnlySpan<char> acceptEncoding)
        {
            if (acceptEncoding.IsEmpty || acceptEncoding.IsWhiteSpace())
            {
                return SoapCompressionEncoding.Identity;
            }

            double brQuality = -1.0;
            double gzipQuality = -1.0;
            double xGzipQuality = -1.0;
            double deflateQuality = -1.0;
            double identityQuality = -1.0;
            double wildcardQuality = -1.0;

            foreach (Range itemRange in acceptEncoding.Split(','))
            {
                ReadOnlySpan<char> item = acceptEncoding[itemRange].Trim();
                if (item.IsEmpty)
                {
                    continue;
                }

                int semiIndex = item.IndexOf(';');
                ReadOnlySpan<char> coding = (semiIndex < 0 ? item : item[..semiIndex]).Trim();
                if (coding.IsEmpty)
                {
                    continue;
                }

                double quality = 1.0;
                if (semiIndex >= 0 && !TryParseQualityParameter(item[(semiIndex + 1)..], out quality))
                {
                    continue;
                }

                if (coding.Equals("br", StringComparison.OrdinalIgnoreCase))
                {
                    brQuality = quality;
                }
                else if (coding.Equals("gzip", StringComparison.OrdinalIgnoreCase))
                {
                    gzipQuality = quality;
                }
                else if (coding.Equals("x-gzip", StringComparison.OrdinalIgnoreCase))
                {
                    xGzipQuality = quality;
                }
                else if (coding.Equals("deflate", StringComparison.OrdinalIgnoreCase))
                {
                    deflateQuality = quality;
                }
                else if (coding.Equals("identity", StringComparison.OrdinalIgnoreCase))
                {
                    identityQuality = quality;
                }
                else if (coding.Equals("*", StringComparison.Ordinal))
                {
                    wildcardQuality = quality;
                }
            }

            double resolvedGzip = gzipQuality >= 0.0 && xGzipQuality >= 0.0
                ? Math.Max(gzipQuality, xGzipQuality)
                : gzipQuality >= 0.0
                    ? gzipQuality
                    : xGzipQuality;

            double effectiveBr = brQuality >= 0.0 ? brQuality : wildcardQuality;
            double effectiveGzip = resolvedGzip >= 0.0 ? resolvedGzip : wildcardQuality;
            double effectiveDeflate = deflateQuality >= 0.0 ? deflateQuality : wildcardQuality;

            double bestQuality = 0.0;
            SoapCompressionEncoding bestEncoding = SoapCompressionEncoding.Identity;

            if (effectiveBr > bestQuality)
            {
                bestQuality = effectiveBr;
                bestEncoding = SoapCompressionEncoding.Brotli;
            }

            if (effectiveGzip > bestQuality)
            {
                bestQuality = effectiveGzip;
                bestEncoding = SoapCompressionEncoding.Gzip;
            }

            if (effectiveDeflate > bestQuality)
            {
                bestQuality = effectiveDeflate;
                bestEncoding = SoapCompressionEncoding.Deflate;
            }

            if (bestEncoding == SoapCompressionEncoding.Identity || identityQuality > bestQuality)
            {
                return SoapCompressionEncoding.Identity;
            }

            return bestEncoding;
        }

        /// <summary>
        /// Returns the canonical HTTP <c>Content-Encoding</c> header token for <paramref name="encoding"/>.
        /// </summary>
        /// <param name="encoding">The compression encoding.</param>
        /// <returns>The lowercase HTTP header token (<c>br</c>, <c>gzip</c>, <c>deflate</c>, or <c>identity</c>).</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="encoding"/> is undefined.</exception>
        public static string ToHeaderValue(SoapCompressionEncoding encoding)
        {
            return encoding switch
            {
                SoapCompressionEncoding.Identity => "identity",
                SoapCompressionEncoding.Gzip => "gzip",
                SoapCompressionEncoding.Brotli => "br",
                SoapCompressionEncoding.Deflate => "deflate",
                _ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding, "Unsupported compression encoding.")
            };
        }

        /// <summary>
        /// Parses a single HTTP <c>Content-Encoding</c> header value and checks whether it is permitted
        /// under <paramref name="allowCompressedRequests"/>.
        /// </summary>
        /// <param name="contentEncoding">The raw <c>Content-Encoding</c> header value, or <see langword="null"/> when omitted.</param>
        /// <param name="allowCompressedRequests">Whether compressed request encodings (<c>gzip</c>, <c>br</c>, <c>deflate</c>) are enabled.</param>
        /// <param name="encoding">Receives the resolved <see cref="SoapCompressionEncoding"/> when supported.</param>
        /// <returns><see langword="true"/> if the encoding is valid and permitted; otherwise <see langword="false"/>.</returns>
        public static bool TryParseRequestContentEncoding(
            string? contentEncoding,
            bool allowCompressedRequests,
            out SoapCompressionEncoding encoding)
        {
            if (string.IsNullOrWhiteSpace(contentEncoding))
            {
                encoding = SoapCompressionEncoding.Identity;

                return true;
            }

            ReadOnlySpan<char> trimmed = contentEncoding.AsSpan().Trim();
            if (trimmed.Equals("identity", StringComparison.OrdinalIgnoreCase))
            {
                encoding = SoapCompressionEncoding.Identity;

                return true;
            }

            if (!allowCompressedRequests)
            {
                encoding = SoapCompressionEncoding.Identity;

                return false;
            }

            if (trimmed.Equals("br", StringComparison.OrdinalIgnoreCase))
            {
                encoding = SoapCompressionEncoding.Brotli;

                return true;
            }

            if (trimmed.Equals("gzip", StringComparison.OrdinalIgnoreCase))
            {
                encoding = SoapCompressionEncoding.Gzip;

                return true;
            }

            if (trimmed.Equals("deflate", StringComparison.OrdinalIgnoreCase))
            {
                encoding = SoapCompressionEncoding.Deflate;

                return true;
            }

            encoding = SoapCompressionEncoding.Identity;

            return false;
        }

        #endregion

        #region Private Methods

        private static void CompressBrotli(
            ReadOnlySpan<byte> uncompressedBytes,
            BoundedPooledBufferWriter destination)
        {
            using BrotliEncoder encoder = new BrotliEncoder(quality: 4, window: 22);
            ReadOnlySpan<byte> remaining = uncompressedBytes;

            while (true)
            {
                int remainingBudget = destination.MaxBytes - destination.WrittenCount;
                Span<byte> outputSpan = destination.GetSpan(Math.Min(256, Math.Max(1, remainingBudget)));
                OperationStatus status = encoder.Compress(
                    remaining,
                    outputSpan,
                    out int bytesConsumed,
                    out int bytesWritten,
                    isFinalBlock: true);

                if (bytesWritten > 0)
                {
                    destination.Advance(bytesWritten);
                }

                remaining = remaining[bytesConsumed..];

                if (status == OperationStatus.Done)
                {
                    break;
                }

                if (status != OperationStatus.DestinationTooSmall)
                {
                    throw new InvalidOperationException($"Brotli compression failed with status {status}.");
                }
            }
        }

        private static SoapRequestBuffer CopyIdentityToBuffer(
            ReadOnlySpan<byte> bytes,
            int maxBytes,
            bool clearOnReturn,
            ArrayPool<byte> pool)
        {
            if (bytes.Length > maxBytes)
            {
                throw new RequestQuotaExceededException(maxBytes);
            }

            int rentalSize = Math.Min(Math.Max(bytes.Length, 1), maxBytes);
            byte[] buffer = pool.Rent(rentalSize);
            int written = 0;

            try
            {
                if (!bytes.IsEmpty)
                {
                    bytes.CopyTo(buffer);
                    written = bytes.Length;
                }

                SoapRequestBuffer owned = new SoapRequestBuffer(buffer, written, clearOnReturn, pool);
                buffer = [];

                return owned;
            }
            finally
            {
                ReturnBufferIfAllocated(pool, buffer, written, clearOnReturn);
            }
        }

        private static SoapRequestBuffer DecompressBrotli(
            ReadOnlySpan<byte> compressedBytes,
            int maxDecompressedBytes,
            bool clearOnReturn,
            ArrayPool<byte> pool)
        {
            long doubledInput = (long)compressedBytes.Length * 2L;
            int initialCapacity = (int)Math.Min(Math.Max(4096L, doubledInput), (long)maxDecompressedBytes);
            byte[] buffer = pool.Rent(initialCapacity);
            int written = 0;

            try
            {
                using BrotliDecoder decoder = new BrotliDecoder();
                ReadOnlySpan<byte> remaining = compressedBytes;
                Span<byte> probe = stackalloc byte[1];

                while (true)
                {
                    int remainingQuota = maxDecompressedBytes - written;
                    Span<byte> destination = buffer.AsSpan(written, Math.Min(buffer.Length - written, remainingQuota));
                    OperationStatus status = decoder.Decompress(
                        remaining,
                        destination,
                        out int bytesConsumed,
                        out int bytesWritten);

                    remaining = remaining[bytesConsumed..];
                    written = checked(written + bytesWritten);

                    if (status == OperationStatus.Done)
                    {
                        if (!remaining.IsEmpty)
                        {
                            throw new InvalidDataException("Trailing data after Brotli-compressed SOAP payload.");
                        }

                        break;
                    }

                    if (status == OperationStatus.InvalidData)
                    {
                        throw new InvalidDataException("Invalid Brotli-compressed SOAP request payload.");
                    }

                    if (status == OperationStatus.DestinationTooSmall)
                    {
                        if (written >= maxDecompressedBytes)
                        {
                            OperationStatus probeStatus = decoder.Decompress(
                                remaining,
                                probe,
                                out int probeConsumed,
                                out int probeWritten);

                            remaining = remaining[probeConsumed..];

                            if (probeWritten > 0 || probeStatus == OperationStatus.DestinationTooSmall)
                            {
                                throw new RequestQuotaExceededException(maxDecompressedBytes);
                            }

                            if (probeStatus == OperationStatus.InvalidData)
                            {
                                throw new InvalidDataException("Invalid Brotli-compressed SOAP request payload.");
                            }

                            if (probeStatus == OperationStatus.Done)
                            {
                                if (!remaining.IsEmpty)
                                {
                                    throw new InvalidDataException("Trailing data after Brotli-compressed SOAP payload.");
                                }

                                break;
                            }

                            throw new InvalidDataException("Truncated Brotli-compressed SOAP request payload.");
                        }

                        long doubledBuffer = (long)buffer.Length * 2L;
                        int desiredCapacity = (int)Math.Min((long)maxDecompressedBytes, Math.Max((long)written + 4096L, doubledBuffer));
                        buffer = SoapRequestBuffer.GrowBuffer(pool, buffer, written, desiredCapacity, maxDecompressedBytes, clearOnReturn);
                        continue;
                    }

                    if (remaining.IsEmpty || (bytesConsumed == 0 && bytesWritten == 0))
                    {
                        throw new InvalidDataException("Truncated or malformed Brotli-compressed SOAP request payload.");
                    }
                }

                SoapRequestBuffer owned = new SoapRequestBuffer(buffer, written, clearOnReturn, pool);
                buffer = [];

                return owned;
            }
            finally
            {
                ReturnBufferIfAllocated(pool, buffer, written, clearOnReturn);
            }
        }

        private static SoapRequestBuffer DecompressStream(
            ReadOnlyMemory<byte> compressedBytes,
            SoapCompressionEncoding encoding,
            int maxDecompressedBytes,
            bool clearOnReturn,
            ArrayPool<byte> pool)
        {
            long doubledInput = (long)compressedBytes.Length * 2L;
            int initialCapacity = (int)Math.Min(Math.Max(4096L, doubledInput), (long)maxDecompressedBytes);
            byte[] buffer = pool.Rent(initialCapacity);
            int written = 0;

            try
            {
                using ReadOnlyMemoryStream input = new ReadOnlyMemoryStream(compressedBytes);
                using Stream decompressor = encoding == SoapCompressionEncoding.Gzip
                    ? new GZipStream(input, CompressionMode.Decompress, leaveOpen: true)
                    : new DeflateStream(input, CompressionMode.Decompress, leaveOpen: true);
                Span<byte> probe = stackalloc byte[1];

                while (true)
                {
                    if (written >= maxDecompressedBytes)
                    {
                        int overflowBytes = decompressor.Read(probe);
                        if (overflowBytes > 0)
                        {
                            throw new RequestQuotaExceededException(maxDecompressedBytes);
                        }

                        break;
                    }

                    if (written == buffer.Length)
                    {
                        long doubledBuffer = (long)buffer.Length * 2L;
                        int desiredCapacity = (int)Math.Min((long)maxDecompressedBytes, Math.Max((long)written + 4096L, doubledBuffer));
                        buffer = SoapRequestBuffer.GrowBuffer(pool, buffer, written, desiredCapacity, maxDecompressedBytes, clearOnReturn);
                    }

                    int remainingQuota = maxDecompressedBytes - written;
                    int bytesRead = decompressor.Read(buffer.AsSpan(written, Math.Min(buffer.Length - written, remainingQuota)));
                    if (bytesRead == 0)
                    {
                        break;
                    }

                    written = checked(written + bytesRead);
                }

                SoapRequestBuffer owned = new SoapRequestBuffer(buffer, written, clearOnReturn, pool);
                buffer = [];

                return owned;
            }
            finally
            {
                ReturnBufferIfAllocated(pool, buffer, written, clearOnReturn);
            }
        }

        private static void ReturnBufferIfAllocated(
            ArrayPool<byte> pool,
            byte[] buffer,
            int written,
            bool clearOnReturn)
        {
            if (buffer.Length == 0)
            {
                return;
            }

            if (clearOnReturn && written > 0)
            {
                buffer.AsSpan(0, written).Clear();
            }

            pool.Return(buffer);
        }

        private static bool TryParseQualityParameter(ReadOnlySpan<char> parameters, out double quality)
        {
            quality = 1.0;

            foreach (Range paramRange in parameters.Split(';'))
            {
                ReadOnlySpan<char> param = parameters[paramRange].Trim();
                if (param.IsEmpty)
                {
                    continue;
                }

                int eqIndex = param.IndexOf('=');
                if (eqIndex <= 0)
                {
                    continue;
                }

                ReadOnlySpan<char> name = param[..eqIndex].Trim();
                if (!name.Equals("q", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ReadOnlySpan<char> value = param[(eqIndex + 1)..].Trim();
                if (!double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double parsed) ||
                    double.IsNaN(parsed) ||
                    parsed < 0.0 ||
                    parsed > 1.0)
                {
                    return false;
                }

                quality = parsed;

                return true;
            }

            return true;
        }

        #endregion

        private sealed class BufferWriterStream : Stream
        {

            #region Fields

            private readonly IBufferWriter<byte> _writer;

            #endregion

            #region Properties

            public override bool CanRead => false;

            public override bool CanSeek => false;

            public override bool CanWrite => true;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            #endregion

            #region Constructors

            public BufferWriterStream(IBufferWriter<byte> writer)
            {
                _writer = writer;
            }

            #endregion

            #region Public Methods

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                ValidateBufferArguments(buffer, offset, count);
                BuffersExtensions.Write(_writer, buffer.AsSpan(offset, count));
            }

            public override void Write(ReadOnlySpan<byte> buffer)
            {
                BuffersExtensions.Write(_writer, buffer);
            }

            #endregion

        }

        private sealed class ReadOnlyMemoryStream : Stream
        {

            #region Fields

            private readonly ReadOnlyMemory<byte> _memory;
            private int _position;

            #endregion

            #region Properties

            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => _memory.Length;

            public override long Position
            {
                get => _position;
                set => throw new NotSupportedException();
            }

            #endregion

            #region Constructors

            public ReadOnlyMemoryStream(ReadOnlyMemory<byte> memory)
            {
                _memory = memory;
            }

            #endregion

            #region Public Methods

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                ValidateBufferArguments(buffer, offset, count);

                return Read(buffer.AsSpan(offset, count));
            }

            public override int Read(Span<byte> buffer)
            {
                int remaining = _memory.Length - _position;
                if (remaining <= 0 || buffer.IsEmpty)
                {
                    return 0;
                }

                int toCopy = Math.Min(remaining, buffer.Length);
                _memory.Span.Slice(_position, toCopy).CopyTo(buffer);
                _position += toCopy;

                return toCopy;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            #endregion

        }

    }

}
