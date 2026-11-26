using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace FastSoap.Core.Buffers
{

    /// <summary>
    /// Disposable owner of a bounded, pool-rented byte buffer populated incrementally from a <see cref="PipeReader"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// using SoapRequestBuffer buffer = await SoapRequestBuffer.CopyFromPipeAsync(
    ///     pipeReader,
    ///     maxBytes: 1024 * 1024,
    ///     cancellationToken: cancellationToken);
    /// ReadOnlySpan&lt;byte&gt; xmlBytes = buffer.Span;
    /// </code>
    /// </example>
    /// <remarks>
    /// Every pipe read advances the <see cref="PipeReader"/> inside a <see langword="finally"/> block so no framework-owned
    /// pipe sequences are retained across awaits or after failure. Logical length is tracked independently of rented capacity.
    /// </remarks>
    public sealed class SoapRequestBuffer : IDisposable
    {

        #region Fields

        private byte[] _buffer;
        private readonly bool _clearOnReturn;
        private bool _disposed;
        private int _length;
        private readonly ArrayPool<byte> _pool;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the underlying rented buffer capacity in bytes.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown when the buffer has been disposed.</exception>
        public int Capacity
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                return _buffer.Length;
            }
        }

        /// <summary>
        /// Gets the logical number of valid request bytes stored in the buffer.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown when the buffer has been disposed.</exception>
        public int Length
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                return _length;
            }
        }

        /// <summary>
        /// Gets a read-only memory slice over the valid request bytes.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown when the buffer has been disposed.</exception>
        public ReadOnlyMemory<byte> Memory
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                return _buffer.AsMemory(0, _length);
            }
        }

        /// <summary>
        /// Gets a read-only span over the valid request bytes.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown when the buffer has been disposed.</exception>
        public ReadOnlySpan<byte> Span
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                return _buffer.AsSpan(0, _length);
            }
        }

        #endregion

        #region Constructors

        private SoapRequestBuffer(byte[] buffer, int length, bool clearOnReturn, ArrayPool<byte> pool)
        {
            _buffer = buffer;
            _length = length;
            _clearOnReturn = clearOnReturn;
            _pool = pool;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Incrementally copies the entire request body from <paramref name="bodyReader"/> into a bounded pooled buffer
        /// up to <paramref name="maxBytes"/>.
        /// </summary>
        /// <param name="bodyReader">The request <see cref="PipeReader"/> to consume without completing or disposing it.</param>
        /// <param name="maxBytes">The maximum permitted logical payload size in bytes.</param>
        /// <param name="initialCapacity">The initial pool rental size hint in bytes.</param>
        /// <param name="clearOnReturn">Whether to zero-clear the used region before returning rented arrays to <paramref name="pool"/>.</param>
        /// <param name="pool">The <see cref="ArrayPool{T}"/> to rent from, or <see langword="null"/> to use <see cref="ArrayPool{T}.Shared"/>.</param>
        /// <param name="cancellationToken">A cancellation token monitored during pipe reads.</param>
        /// <returns>An owned <see cref="SoapRequestBuffer"/> containing the complete bounded request bytes.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="bodyReader"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxBytes"/> or <paramref name="initialCapacity"/> is less than or equal to zero.</exception>
        /// <exception cref="RequestQuotaExceededException">Thrown when incoming bytes exceed <paramref name="maxBytes"/>.</exception>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled or the pipe read is canceled.</exception>
        public static async ValueTask<SoapRequestBuffer> CopyFromPipeAsync(
            PipeReader bodyReader,
            int maxBytes,
            int initialCapacity = 4096,
            bool clearOnReturn = true,
            ArrayPool<byte>? pool = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(bodyReader);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxBytes, 0);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(initialCapacity, 0);

            ArrayPool<byte> effectivePool = pool ?? ArrayPool<byte>.Shared;
            int firstRentalSize = Math.Min(initialCapacity, maxBytes);
            byte[] buffer = effectivePool.Rent(firstRentalSize);
            int written = 0;

            try
            {
                while (true)
                {
                    ReadResult result = await bodyReader.ReadAsync(cancellationToken).ConfigureAwait(false);
                    ReadOnlySequence<byte> sequence = result.Buffer;
                    SequencePosition consumed = sequence.Start;
                    bool completed = result.IsCompleted;
                    bool canceled = result.IsCanceled;

                    try
                    {
                        if (canceled)
                        {
                            throw new OperationCanceledException(cancellationToken);
                        }

                        long segmentLength = sequence.Length;
                        if (segmentLength > 0)
                        {
                            long remainingQuota = (long)maxBytes - written;
                            if (segmentLength > remainingQuota)
                            {
                                throw new RequestQuotaExceededException(maxBytes);
                            }

                            int nextLength = checked(written + (int)segmentLength);
                            if (nextLength > buffer.Length)
                            {
                                buffer = GrowBuffer(effectivePool, buffer, written, nextLength, maxBytes, clearOnReturn);
                            }

                            sequence.CopyTo(buffer.AsSpan(written));
                            written = nextLength;
                        }

                        consumed = sequence.End;
                    }
                    finally
                    {
                        bodyReader.AdvanceTo(consumed);
                    }

                    if (completed)
                    {
                        break;
                    }
                }

                SoapRequestBuffer owned = new SoapRequestBuffer(buffer, written, clearOnReturn, effectivePool);
                buffer = [];

                return owned;
            }
            finally
            {
                if (buffer.Length > 0)
                {
                    if (clearOnReturn && written > 0)
                    {
                        buffer.AsSpan(0, written).Clear();
                    }

                    effectivePool.Return(buffer);
                }
            }
        }

        /// <summary>
        /// Zero-clears used bytes when configured and returns the owned buffer to its originating pool exactly once.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            byte[] toReturn = _buffer;
            int usedLength = _length;
            _buffer = [];
            _length = 0;

            if (toReturn.Length > 0)
            {
                if (_clearOnReturn && usedLength > 0)
                {
                    toReturn.AsSpan(0, usedLength).Clear();
                }

                _pool.Return(toReturn);
            }
        }

        /// <summary>
        /// Validates a known HTTP <c>Content-Length</c> header against <paramref name="maxBytes"/> before renting buffers.
        /// </summary>
        /// <param name="contentLength">The declared <c>Content-Length</c> header value, if present.</param>
        /// <param name="maxBytes">The maximum permitted request size in bytes.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxBytes"/> is less than or equal to zero.</exception>
        /// <exception cref="RequestQuotaExceededException">Thrown when <paramref name="contentLength"/> exceeds <paramref name="maxBytes"/>.</exception>
        public static void ValidateContentLength(long? contentLength, int maxBytes)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxBytes, 0);

            if (contentLength is long declaredLength && declaredLength > maxBytes)
            {
                throw new RequestQuotaExceededException(maxBytes);
            }
        }

        #endregion

        #region Private Methods

        private static byte[] GrowBuffer(
            ArrayPool<byte> pool,
            byte[] current,
            int written,
            int requiredLength,
            int maxBytes,
            bool clearOnReturn)
        {
            long doubled = (long)current.Length * 2L;
            int targetCapacity = checked((int)Math.Min(maxBytes, Math.Max(doubled, requiredLength)));
            byte[] replacement = pool.Rent(targetCapacity);

            try
            {
                current.AsSpan(0, written).CopyTo(replacement);
            }
            catch
            {
                pool.Return(replacement);
                throw;
            }

            if (clearOnReturn && written > 0)
            {
                current.AsSpan(0, written).Clear();
            }

            pool.Return(current);

            return replacement;
        }

        #endregion

    }

}
