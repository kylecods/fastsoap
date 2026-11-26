using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace FastSoap.Core.Buffers
{

    /// <summary>
    /// Bounded, pool-backed <see cref="IBufferWriter{T}"/> that stages a complete SOAP response or fault before committing
    /// to a downstream <see cref="PipeWriter"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// using BoundedPooledBufferWriter writer = new BoundedPooledBufferWriter(4096, maxBytes: 2 * 1024 * 1024);
    /// BuffersExtensions.Write(writer, "&lt;ok/&gt;"u8);
    /// await writer.CommitToAsync(pipeWriter, cancellationToken);
    /// </code>
    /// </example>
    /// <remarks>
    /// Enforces <see cref="MaxBytes"/> independently of pool bucket rounding, clears used bytes on disposal when
    /// configured, and copies staged bytes via <see cref="BuffersExtensions.Write{T}(IBufferWriter{T}, ReadOnlySpan{T})"/>
    /// rather than requesting a single oversized contiguous span from the destination pipe.
    /// </remarks>
    public sealed class BoundedPooledBufferWriter : IBufferWriter<byte>, IDisposable
    {

        #region Fields

        private byte[] _buffer;
        private readonly bool _clearOnReturn;
        private bool _disposed;
        private readonly int _maxBytes;
        private readonly ArrayPool<byte> _pool;
        private int _written;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the underlying rented array capacity in bytes.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown when the writer has been disposed.</exception>
        public int Capacity
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                return _buffer.Length;
            }
        }

        /// <summary>
        /// Gets the maximum allowed staged byte count.
        /// </summary>
        public int MaxBytes => _maxBytes;

        /// <summary>
        /// Gets the logical number of bytes written to the staging buffer.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown when the writer has been disposed.</exception>
        public int WrittenCount
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                return _written;
            }
        }

        /// <summary>
        /// Gets a read-only memory slice of all staged bytes written so far.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown when the writer has been disposed.</exception>
        public ReadOnlyMemory<byte> WrittenMemory
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                return _buffer.AsMemory(0, _written);
            }
        }

        /// <summary>
        /// Gets a read-only span of all staged bytes written so far.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown when the writer has been disposed.</exception>
        public ReadOnlySpan<byte> WrittenSpan
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                return _buffer.AsSpan(0, _written);
            }
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new <see cref="BoundedPooledBufferWriter"/> with the specified capacity bounds and pool.
        /// </summary>
        /// <param name="initialCapacity">Initial pool rental hint in bytes.</param>
        /// <param name="maxBytes">Maximum allowed logical staged bytes before throwing <see cref="ResponseQuotaExceededException"/>.</param>
        /// <param name="clearOnReturn">Whether to zero-clear used bytes before returning buffers to <paramref name="pool"/>.</param>
        /// <param name="pool">The <see cref="ArrayPool{T}"/> to rent from, or <see langword="null"/> to use <see cref="ArrayPool{T}.Shared"/>.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="initialCapacity"/> or <paramref name="maxBytes"/> is less than or equal to zero.</exception>
        public BoundedPooledBufferWriter(
            int initialCapacity,
            int maxBytes,
            bool clearOnReturn = true,
            ArrayPool<byte>? pool = null)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(initialCapacity, 0);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxBytes, 0);

            _maxBytes = maxBytes;
            _clearOnReturn = clearOnReturn;
            _pool = pool ?? ArrayPool<byte>.Shared;

            int rentalSize = Math.Min(initialCapacity, maxBytes);
            _buffer = _pool.Rent(rentalSize);
        }

        #endregion

        #region Public Methods

        /// <inheritdoc />
        public void Advance(int count)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(count);

            if (count > _buffer.Length - _written)
            {
                throw new InvalidOperationException("Cannot advance past the end of the rented buffer.");
            }

            int nextWritten = checked(_written + count);
            if (nextWritten > _maxBytes)
            {
                throw new ResponseQuotaExceededException(_maxBytes);
            }

            _written = nextWritten;
        }

        /// <summary>
        /// Writes all staged bytes to <paramref name="destination"/> using
        /// <see cref="BuffersExtensions.Write{T}(IBufferWriter{T}, ReadOnlySpan{T})"/> and flushes the pipe.
        /// </summary>
        /// <param name="destination">The downstream <see cref="PipeWriter"/> to receive staged bytes.</param>
        /// <param name="cancellationToken">A cancellation token monitored during pipe flushing.</param>
        /// <returns>A <see cref="ValueTask"/> representing the asynchronous write and flush operation.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the writer has been disposed.</exception>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="destination"/> is <see langword="null"/>.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the pipe flush is canceled.</exception>
        public async ValueTask CommitToAsync(PipeWriter destination, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(destination);

            if (_written > 0)
            {
                BuffersExtensions.Write(destination, _buffer.AsSpan(0, _written));
            }

            FlushResult result = await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            if (result.IsCanceled)
            {
                throw new OperationCanceledException(cancellationToken);
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
            int usedLength = _written;
            _buffer = [];
            _written = 0;

            if (toReturn.Length > 0)
            {
                if (_clearOnReturn && usedLength > 0)
                {
                    toReturn.AsSpan(0, usedLength).Clear();
                }

                _pool.Return(toReturn);
            }
        }

        /// <inheritdoc />
        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);

            return _buffer.AsMemory(_written);
        }

        /// <inheritdoc />
        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);

            return _buffer.AsSpan(_written);
        }

        #endregion

        #region Private Methods

        private void EnsureCapacity(int sizeHint)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);

            int needed = sizeHint == 0 ? 1 : sizeHint;
            long requiredTotal = (long)_written + needed;
            if (requiredTotal > _maxBytes)
            {
                throw new ResponseQuotaExceededException(_maxBytes);
            }

            int available = _buffer.Length - _written;
            if (available >= needed)
            {
                return;
            }

            long doubled = (long)_buffer.Length * 2L;
            int targetCapacity = checked((int)Math.Min(_maxBytes, Math.Max(doubled, requiredTotal)));
            byte[] replacement = _pool.Rent(targetCapacity);
            byte[] previous = _buffer;

            try
            {
                previous.AsSpan(0, _written).CopyTo(replacement);
            }
            catch
            {
                _pool.Return(replacement);
                throw;
            }

            _buffer = replacement;

            if (_clearOnReturn && _written > 0)
            {
                previous.AsSpan(0, _written).Clear();
            }

            _pool.Return(previous);
        }

        #endregion

    }

}
