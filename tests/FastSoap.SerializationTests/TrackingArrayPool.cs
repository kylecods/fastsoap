using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;

namespace FastSoap.SerializationTests
{

    /// <summary>
    /// Deterministic <see cref="ArrayPool{T}"/> test double that enforces single-return ownership,
    /// rejects foreign arrays, and tracks peak live rented memory without mocking frameworks.
    /// </summary>
    internal sealed class TrackingArrayPool : ArrayPool<byte>
    {

        #region Fields

        private readonly Dictionary<byte[], int> _activeRentals = new Dictionary<byte[], int>(ReferenceEqualityComparer.Instance);
        private int _activeBytes;
        private readonly Lock _lock = new Lock();
        private readonly int _minimumBucketSize;
        private int _peakBytes;
        private readonly List<byte[]> _returnedBuffers = new List<byte[]>();
        private int _totalRentCalls;
        private int _totalReturnCalls;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the current number of outstanding rented buffers.
        /// </summary>
        public int ActiveRentalCount
        {
            get
            {
                lock (_lock)
                {
                    return _activeRentals.Count;
                }
            }
        }

        /// <summary>
        /// Gets the current sum of active rented array capacities in bytes.
        /// </summary>
        public int CurrentRentedBytes
        {
            get
            {
                lock (_lock)
                {
                    return _activeBytes;
                }
            }
        }

        /// <summary>
        /// Gets the peak sum of simultaneously live rented array capacities in bytes.
        /// </summary>
        public int PeakRentedBytes
        {
            get
            {
                lock (_lock)
                {
                    return _peakBytes;
                }
            }
        }

        /// <summary>
        /// Gets all buffer instances that have been returned to this pool in return order.
        /// </summary>
        public IReadOnlyList<byte[]> ReturnedBuffers
        {
            get
            {
                lock (_lock)
                {
                    return _returnedBuffers.ToArray();
                }
            }
        }

        /// <summary>
        /// Gets or sets an optional 1-based rent call number on which <see cref="Rent"/> throws
        /// <see cref="InvalidOperationException"/> to exercise failure cleanup paths.
        /// </summary>
        public int? ThrowOnRentCallNumber { get; set; }

        /// <summary>
        /// Gets the total number of successful <see cref="Rent"/> calls.
        /// </summary>
        public int TotalRentCalls
        {
            get
            {
                lock (_lock)
                {
                    return _totalRentCalls;
                }
            }
        }

        /// <summary>
        /// Gets the total number of <see cref="Return"/> calls.
        /// </summary>
        public int TotalReturnCalls
        {
            get
            {
                lock (_lock)
                {
                    return _totalReturnCalls;
                }
            }
        }

        #endregion

        #region Constructors

        public TrackingArrayPool(int minimumBucketSize = 16)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(minimumBucketSize, 0);
            _minimumBucketSize = minimumBucketSize;
        }

        #endregion

        #region Public Methods

        public override byte[] Rent(int minimumLength)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(minimumLength);

            lock (_lock)
            {
                int callNumber = _totalRentCalls + 1;
                if (ThrowOnRentCallNumber == callNumber)
                {
                    throw new InvalidOperationException($"Simulated pool rent failure on call #{callNumber}.");
                }

                int bucketSize = RoundUpBucket(Math.Max(minimumLength, _minimumBucketSize));
                byte[] buffer = new byte[bucketSize];
                Array.Fill(buffer, (byte)0xAA);

                _totalRentCalls++;
                _activeRentals.Add(buffer, bucketSize);
                _activeBytes += bucketSize;
                if (_activeBytes > _peakBytes)
                {
                    _peakBytes = _activeBytes;
                }

                return buffer;
            }
        }

        public override void Return(byte[] array, bool clearArray = false)
        {
            ArgumentNullException.ThrowIfNull(array);

            lock (_lock)
            {
                if (!_activeRentals.Remove(array, out int bucketSize))
                {
                    if (_returnedBuffers.Contains(array))
                    {
                        throw new InvalidOperationException("Double return detected on TrackingArrayPool.");
                    }

                    throw new InvalidOperationException("Foreign buffer returned to TrackingArrayPool.");
                }

                if (clearArray)
                {
                    Array.Clear(array);
                }

                _totalReturnCalls++;
                _activeBytes -= bucketSize;
                _returnedBuffers.Add(array);
            }
        }

        #endregion

        #region Private Methods

        private static int RoundUpBucket(int size)
        {
            int bucket = 16;
            while (bucket < size && bucket < (1 << 30))
            {
                bucket <<= 1;
            }

            return Math.Max(bucket, size);
        }

        #endregion

    }

}
