using System;
using System.Buffers;

namespace FastSoap.R0C.GreenfieldSpike.Buffers
{

    /// <summary>
    /// Bounded pooled <see cref="IBufferWriter{T}"/> that stages an entire SOAP response before committing to the HTTP response pipe.
    /// </summary>
    /// <remarks>
    /// Owns a single rented array from <see cref="ArrayPool{T}.Shared"/> at a time, enforces <see cref="MaxBytes"/>
    /// before growing, and returns any rented array on disposal.
    /// </remarks>
    public sealed class BoundedPooledBufferWriter : IBufferWriter<byte>, IDisposable
    {

        #region Fields

        private byte[] _buffer;
        private readonly bool _clearOnReturn;
        private bool _disposed;
        private readonly int _maxBytes;
        private int _written;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the maximum allowed staged byte count.
        /// </summary>
        public int MaxBytes => _maxBytes;

        /// <summary>
        /// Gets the slice of staged bytes written so far.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown if the writer has been disposed.</exception>
        public ReadOnlyMemory<byte> WrittenMemory
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _buffer.AsMemory(0, _written);
            }
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new <see cref="BoundedPooledBufferWriter"/> with the specified capacity bounds.
        /// </summary>
        /// <param name="initialCapacity">Initial buffer rental size in bytes.</param>
        /// <param name="maxBytes">Maximum allowed bytes before throwing <see cref="ResponseQuotaExceededException"/>.</param>
        /// <param name="clearOnReturn">Whether to clear used bytes before returning buffers to the pool.</param>
        public BoundedPooledBufferWriter(int initialCapacity, int maxBytes, bool clearOnReturn)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity);
            ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, initialCapacity);

            _maxBytes = maxBytes;
            _clearOnReturn = clearOnReturn;
            _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
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

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            byte[] toReturn = _buffer;
            _buffer = [];
            if (toReturn.Length > 0)
            {
                if (_clearOnReturn && _written > 0)
                {
                    toReturn.AsSpan(0, _written).Clear();
                }

                ArrayPool<byte>.Shared.Return(toReturn);
            }

            _written = 0;
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
            int available = _buffer.Length - _written;
            if (available >= needed)
            {
                return;
            }

            long requiredTotal = (long)_written + needed;
            if (requiredTotal > _maxBytes)
            {
                throw new ResponseQuotaExceededException(_maxBytes);
            }

            long doubled = (long)_buffer.Length * 2L;
            int targetCapacity = (int)Math.Min(_maxBytes, Math.Max(doubled, requiredTotal));
            byte[] replacement = ArrayPool<byte>.Shared.Rent(targetCapacity);
            byte[] previous = _buffer;

            previous.AsSpan(0, _written).CopyTo(replacement);
            _buffer = replacement;

            if (_clearOnReturn && _written > 0)
            {
                previous.AsSpan(0, _written).Clear();
            }

            ArrayPool<byte>.Shared.Return(previous);
        }

        #endregion

    }

    /// <summary>
    /// Exception thrown when a staged SOAP response exceeds the configured byte limit.
    /// </summary>
    public sealed class ResponseQuotaExceededException : Exception
    {

        #region Constructors

        /// <summary>
        /// Initializes a new instance of <see cref="ResponseQuotaExceededException"/>.
        /// </summary>
        /// <param name="maxBytes">The configured byte limit that was exceeded.</param>
        public ResponseQuotaExceededException(int maxBytes)
            : base($"Staged SOAP response exceeded the maximum limit of {maxBytes} bytes.")
        {
        }

        #endregion

    }

}
