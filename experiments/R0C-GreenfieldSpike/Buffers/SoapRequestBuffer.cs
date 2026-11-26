using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace FastSoap.R0C.GreenfieldSpike.Buffers
{

    /// <summary>
    /// Exception thrown when an incoming SOAP HTTP request exceeds the configured byte quota.
    /// </summary>
    public sealed class RequestQuotaExceededException : Exception
    {

        #region Constructors

        /// <summary>
        /// Initializes a new instance of <see cref="RequestQuotaExceededException"/>.
        /// </summary>
        /// <param name="maxBytes">The configured request byte limit.</param>
        public RequestQuotaExceededException(int maxBytes)
            : base($"SOAP request body exceeded the maximum limit of {maxBytes} bytes.")
        {
        }

        #endregion

    }

    /// <summary>
    /// Disposable owner of a bounded, pool-rented byte buffer populated incrementally from a <see cref="PipeReader"/>.
    /// </summary>
    /// <remarks>
    /// Every pipe read advances the <see cref="PipeReader"/> in a <see langword="finally"/> block so no framework-owned
    /// pipe segments are retained across awaits or after failure.
    /// </remarks>
    public sealed class SoapRequestBuffer : IDisposable
    {

        #region Fields

        private byte[] _buffer;
        private readonly bool _clearOnReturn;
        private bool _disposed;
        private int _length;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the read-only span of valid request bytes read into the buffer.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown if the buffer has been disposed.</exception>
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

        private SoapRequestBuffer(byte[] buffer, int length, bool clearOnReturn)
        {
            _buffer = buffer;
            _length = length;
            _clearOnReturn = clearOnReturn;
        }

        #endregion

        #region Public Methods

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
                if (_clearOnReturn && _length > 0)
                {
                    toReturn.AsSpan(0, _length).Clear();
                }

                ArrayPool<byte>.Shared.Return(toReturn);
            }

            _length = 0;
        }

        /// <summary>
        /// Incrementally reads the entire request body from <paramref name="bodyReader"/> up to <paramref name="maxBytes"/>.
        /// </summary>
        /// <param name="bodyReader">The ASP.NET Core request pipe reader.</param>
        /// <param name="maxBytes">The maximum allowed request body size in bytes.</param>
        /// <param name="initialCapacity">The initial pool rental size in bytes.</param>
        /// <param name="clearOnReturn">Whether to zero used bytes before returning arrays to the pool.</param>
        /// <param name="cancellationToken">Cancellation token for request abort.</param>
        /// <returns>An owned <see cref="SoapRequestBuffer"/> containing the complete bounded payload.</returns>
        public static async ValueTask<SoapRequestBuffer> ReadBoundedAsync(
            PipeReader bodyReader,
            int maxBytes,
            int initialCapacity,
            bool clearOnReturn,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(bodyReader);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity);

            int firstRentalSize = Math.Min(initialCapacity, maxBytes);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(firstRentalSize);
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
                                buffer = GrowBuffer(buffer, written, nextLength, maxBytes, clearOnReturn);
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

                SoapRequestBuffer owned = new SoapRequestBuffer(buffer, written, clearOnReturn);
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

                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }

        #endregion

        #region Private Methods

        private static byte[] GrowBuffer(byte[] current, int written, int requiredLength, int maxBytes, bool clearOnReturn)
        {
            long doubled = (long)current.Length * 2L;
            int targetCapacity = (int)Math.Min(maxBytes, Math.Max(doubled, requiredLength));
            byte[] replacement = ArrayPool<byte>.Shared.Rent(targetCapacity);

            current.AsSpan(0, written).CopyTo(replacement);
            if (clearOnReturn && written > 0)
            {
                current.AsSpan(0, written).Clear();
            }

            ArrayPool<byte>.Shared.Return(current);
            return replacement;
        }

        #endregion

    }

}
