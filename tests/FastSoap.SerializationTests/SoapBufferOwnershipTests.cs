using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

using FastSoap.Core.Buffers;

using Xunit;

namespace FastSoap.SerializationTests
{

    /// <summary>
    /// Verifies buffer ownership, pool accounting, quota enforcement, clearing policy, and pipe consumption
    /// for <see cref="SoapRequestBuffer"/> and <see cref="BoundedPooledBufferWriter"/>.
    /// </summary>
    public sealed class SoapBufferOwnershipTests
    {

        #region Public Methods

        [Fact]
        public async Task BoundedPooledBufferWriter_CommitToAsync_UsesMultipleWritesInsteadOfSingleFullLengthSpan()
        {
            TrackingArrayPool pool = new TrackingArrayPool();
            byte[] payload = new byte[9000];
            for (int index = 0; index < payload.Length; index++)
            {
                payload[index] = (byte)(index % 251);
            }

            RecordingPipeWriter pipeWriter = new RecordingPipeWriter();
            using (BoundedPooledBufferWriter writer = new BoundedPooledBufferWriter(256, 16384, clearOnReturn: true, pool))
            {
                BuffersExtensions.Write(writer, payload);
                await writer.CommitToAsync(pipeWriter, TestContext.Current.CancellationToken);
            }

            Assert.Equal(0, pool.ActiveRentalCount);
            Assert.Equal(1, pipeWriter.FlushCount);
            Assert.True(pipeWriter.GetSpanRequests.Count > 1);
            Assert.All(pipeWriter.GetSpanRequests, static requested => Assert.True(requested < 9000));
            Assert.True(pipeWriter.WrittenBytes.AsSpan().SequenceEqual(payload));
        }

        [Fact]
        public void BoundedPooledBufferWriter_Dispose_ClearsUsedBytesWhenConfiguredAndIsDoubleDisposeSafe()
        {
            TrackingArrayPool clearedPool = new TrackingArrayPool();
            byte[] secret = "SensitiveSoapPayload"u8.ToArray();

            BoundedPooledBufferWriter clearedWriter = new BoundedPooledBufferWriter(64, 256, clearOnReturn: true, clearedPool);
            BuffersExtensions.Write(clearedWriter, secret);
            clearedWriter.Dispose();
            clearedWriter.Dispose();

            Assert.Equal(0, clearedPool.ActiveRentalCount);
            Assert.Equal(1, clearedPool.TotalReturnCalls);
            Assert.All(clearedPool.ReturnedBuffers[0].AsSpan(0, secret.Length).ToArray(), static b => Assert.Equal((byte)0, b));
            Assert.Throws<ObjectDisposedException>(() => clearedWriter.WrittenCount);
            Assert.Throws<ObjectDisposedException>(() => _ = clearedWriter.WrittenMemory);
            Assert.Throws<ObjectDisposedException>(() => _ = clearedWriter.Capacity);
            Assert.Throws<ObjectDisposedException>(() => clearedWriter.Advance(0));

            TrackingArrayPool unclearedPool = new TrackingArrayPool();
            using (BoundedPooledBufferWriter unclearedWriter = new BoundedPooledBufferWriter(64, 256, clearOnReturn: false, unclearedPool))
            {
                BuffersExtensions.Write(unclearedWriter, secret);
            }

            Assert.Equal(0, unclearedPool.ActiveRentalCount);
            Assert.True(unclearedPool.ReturnedBuffers[0].AsSpan(0, secret.Length).SequenceEqual(secret));
        }

        [Fact]
        public void BoundedPooledBufferWriter_ExactQuotaAndQuotaPlusOne_EnforcedIndependentlyOfBucketCapacity()
        {
            TrackingArrayPool pool = new TrackingArrayPool(minimumBucketSize: 64);

            using (BoundedPooledBufferWriter exactWriter = new BoundedPooledBufferWriter(32, maxBytes: 100, clearOnReturn: true, pool))
            {
                BuffersExtensions.Write(exactWriter, new byte[100]);
                Assert.Equal(100, exactWriter.WrittenCount);
                Assert.True(exactWriter.Capacity >= 100);
            }

            Assert.Equal(0, pool.ActiveRentalCount);

            using BoundedPooledBufferWriter overflowWriter = new BoundedPooledBufferWriter(32, maxBytes: 100, clearOnReturn: true, pool);
            BuffersExtensions.Write(overflowWriter, new byte[100]);
            ResponseQuotaExceededException ex = Assert.Throws<ResponseQuotaExceededException>(
                () => overflowWriter.GetSpan(1));
            Assert.Equal(100, ex.MaxBytes);

            overflowWriter.Dispose();
            Assert.Equal(0, pool.ActiveRentalCount);
        }

        [Fact]
        public void BoundedPooledBufferWriter_GrowthFailure_ReturnsOriginalBuffer()
        {
            TrackingArrayPool pool = new TrackingArrayPool(minimumBucketSize: 16);

            using BoundedPooledBufferWriter writer = new BoundedPooledBufferWriter(16, maxBytes: 256, clearOnReturn: true, pool);
            BuffersExtensions.Write(writer, "1234567890123456"u8);

            pool.ThrowOnRentCallNumber = 2;
            Assert.Throws<InvalidOperationException>(() => writer.GetSpan(32));

            writer.Dispose();
            Assert.Equal(0, pool.ActiveRentalCount);
            Assert.Equal(1, pool.TotalRentCalls);
            Assert.Equal(1, pool.TotalReturnCalls);
        }

        [Fact]
        public void BoundedPooledBufferWriter_GrowthPeakAccounting_TracksSimultaneousRentals()
        {
            TrackingArrayPool pool = new TrackingArrayPool(minimumBucketSize: 16);

            using (BoundedPooledBufferWriter writer = new BoundedPooledBufferWriter(16, maxBytes: 256, clearOnReturn: true, pool))
            {
                Span<byte> first = writer.GetSpan(16);
                first[..16].Fill(0x11);
                writer.Advance(16);

                Span<byte> second = writer.GetSpan(32);
                second[..32].Fill(0x22);
                writer.Advance(32);

                Assert.Equal(48, writer.WrittenCount);
                Assert.True(pool.PeakRentedBytes >= 16 + 64);
            }

            Assert.Equal(0, pool.ActiveRentalCount);
            Assert.Equal(2, pool.TotalRentCalls);
            Assert.Equal(2, pool.TotalReturnCalls);
        }

        [Fact]
        public async Task SoapRequestBuffer_CopyFromPipeAsync_EmptyInputAndMultiSegmentGrowth()
        {
            TrackingArrayPool pool = new TrackingArrayPool(minimumBucketSize: 16);

            Pipe emptyPipe = new Pipe();
            await emptyPipe.Writer.CompleteAsync();
            using (SoapRequestBuffer emptyBuffer = await SoapRequestBuffer.CopyFromPipeAsync(
                emptyPipe.Reader,
                maxBytes: 1024,
                initialCapacity: 16,
                clearOnReturn: true,
                pool: pool,
                cancellationToken: TestContext.Current.CancellationToken))
            {
                Assert.Equal(0, emptyBuffer.Length);
                Assert.True(emptyBuffer.Span.IsEmpty);
            }

            Assert.Equal(0, pool.ActiveRentalCount);

            Pipe multiSegmentPipe = new Pipe();
            byte[] part1 = "0123456789ABCDEF"u8.ToArray();
            byte[] part2 = "GHIJKLMNOPQRSTUV"u8.ToArray();
            byte[] part3 = "WXYZ0123456789ab"u8.ToArray();
            await multiSegmentPipe.Writer.WriteAsync(part1, TestContext.Current.CancellationToken);
            await multiSegmentPipe.Writer.FlushAsync(TestContext.Current.CancellationToken);
            await multiSegmentPipe.Writer.WriteAsync(part2, TestContext.Current.CancellationToken);
            await multiSegmentPipe.Writer.WriteAsync(part3, TestContext.Current.CancellationToken);
            await multiSegmentPipe.Writer.CompleteAsync();

            using (SoapRequestBuffer multiBuffer = await SoapRequestBuffer.CopyFromPipeAsync(
                multiSegmentPipe.Reader,
                maxBytes: 128,
                initialCapacity: 16,
                clearOnReturn: true,
                pool: pool,
                cancellationToken: TestContext.Current.CancellationToken))
            {
                Assert.Equal(48, multiBuffer.Length);
                Assert.Equal("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789ab", System.Text.Encoding.UTF8.GetString(multiBuffer.Span));
                Assert.True(pool.PeakRentedBytes > multiBuffer.Capacity);
            }

            Assert.Equal(0, pool.ActiveRentalCount);
            Assert.Equal(pool.TotalRentCalls, pool.TotalReturnCalls);
        }

        [Fact]
        public async Task SoapRequestBuffer_Dispose_ClearsUsedBytesAndIsDoubleDisposeSafe()
        {
            TrackingArrayPool pool = new TrackingArrayPool(minimumBucketSize: 32);
            Pipe pipe = new Pipe();
            byte[] secret = "Account=123456;Pin=9999"u8.ToArray();
            await pipe.Writer.WriteAsync(secret, TestContext.Current.CancellationToken);
            await pipe.Writer.CompleteAsync();

            SoapRequestBuffer buffer = await SoapRequestBuffer.CopyFromPipeAsync(
                pipe.Reader,
                maxBytes: 128,
                initialCapacity: 32,
                clearOnReturn: true,
                pool: pool,
                cancellationToken: TestContext.Current.CancellationToken);

            buffer.Dispose();
            buffer.Dispose();

            Assert.Equal(0, pool.ActiveRentalCount);
            Assert.Equal(1, pool.TotalReturnCalls);
            Assert.All(pool.ReturnedBuffers[0].AsSpan(0, secret.Length).ToArray(), static b => Assert.Equal((byte)0, b));
            Assert.Throws<ObjectDisposedException>(() => buffer.Length);
            Assert.Throws<ObjectDisposedException>(() => buffer.Capacity);
            Assert.Throws<ObjectDisposedException>(() => _ = buffer.Memory);
            Assert.Throws<ObjectDisposedException>(() => _ = buffer.Span.Length);
        }

        [Fact]
        public async Task SoapRequestBuffer_ExactQuotaAndQuotaPlusOne_EnforcedAndCleansUpOnFailure()
        {
            TrackingArrayPool pool = new TrackingArrayPool(minimumBucketSize: 64);

            Pipe exactPipe = new Pipe();
            await exactPipe.Writer.WriteAsync(new byte[100], TestContext.Current.CancellationToken);
            await exactPipe.Writer.CompleteAsync();

            using (SoapRequestBuffer exact = await SoapRequestBuffer.CopyFromPipeAsync(
                exactPipe.Reader,
                maxBytes: 100,
                initialCapacity: 32,
                clearOnReturn: true,
                pool: pool,
                cancellationToken: TestContext.Current.CancellationToken))
            {
                Assert.Equal(100, exact.Length);
                Assert.True(exact.Capacity >= 100);
            }

            Assert.Equal(0, pool.ActiveRentalCount);

            Pipe overPipe = new Pipe();
            byte[] firstChunk = new byte[50];
            Array.Fill(firstChunk, (byte)0x42);

            ValueTask<SoapRequestBuffer> readTask = SoapRequestBuffer.CopyFromPipeAsync(
                overPipe.Reader,
                maxBytes: 100,
                initialCapacity: 32,
                clearOnReturn: true,
                pool: pool,
                cancellationToken: TestContext.Current.CancellationToken);

            await overPipe.Writer.WriteAsync(firstChunk, TestContext.Current.CancellationToken);
            await overPipe.Writer.FlushAsync(TestContext.Current.CancellationToken);
            await Task.Delay(20, TestContext.Current.CancellationToken);
            await overPipe.Writer.WriteAsync(new byte[51], TestContext.Current.CancellationToken);
            await overPipe.Writer.CompleteAsync();

            RequestQuotaExceededException ex = await Assert.ThrowsAsync<RequestQuotaExceededException>(async () =>
                await readTask);

            Assert.Equal(100, ex.MaxBytes);
            Assert.Equal(0, pool.ActiveRentalCount);
            Assert.All(pool.ReturnedBuffers[^1].AsSpan(0, 50).ToArray(), static b => Assert.Equal((byte)0, b));

            SoapRequestBuffer.ValidateContentLength(100, 100);
            RequestQuotaExceededException headerEx = Assert.Throws<RequestQuotaExceededException>(
                static () => SoapRequestBuffer.ValidateContentLength(101, 100));
            Assert.Equal(100, headerEx.MaxBytes);
        }

        [Fact]
        public async Task SoapRequestBuffer_GrowthException_ReturnsRentedBuffer()
        {
            TrackingArrayPool pool = new TrackingArrayPool(minimumBucketSize: 16);
            Pipe pipe = new Pipe();
            await pipe.Writer.WriteAsync(new byte[16], TestContext.Current.CancellationToken);
            await pipe.Writer.FlushAsync(TestContext.Current.CancellationToken);
            await pipe.Writer.WriteAsync(new byte[32], TestContext.Current.CancellationToken);
            await pipe.Writer.CompleteAsync();

            pool.ThrowOnRentCallNumber = 2;

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await SoapRequestBuffer.CopyFromPipeAsync(
                    pipe.Reader,
                    maxBytes: 128,
                    initialCapacity: 16,
                    clearOnReturn: true,
                    pool: pool,
                    cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal(0, pool.ActiveRentalCount);
            Assert.Equal(1, pool.TotalRentCalls);
            Assert.Equal(1, pool.TotalReturnCalls);
        }

        [Fact]
        public void TrackingArrayPool_RejectsForeignAndDoubleReturns()
        {
            TrackingArrayPool pool = new TrackingArrayPool();
            byte[] foreign = new byte[64];

            InvalidOperationException foreignEx = Assert.Throws<InvalidOperationException>(() => pool.Return(foreign));
            Assert.Equal("Foreign buffer returned to TrackingArrayPool.", foreignEx.Message);

            byte[] rented = pool.Rent(32);
            pool.Return(rented);

            InvalidOperationException doubleEx = Assert.Throws<InvalidOperationException>(() => pool.Return(rented));
            Assert.Equal("Double return detected on TrackingArrayPool.", doubleEx.Message);
        }

        #endregion

        private sealed class RecordingPipeWriter : PipeWriter
        {

            #region Fields

            private readonly List<int> _getSpanRequests = new List<int>();
            private readonly List<byte> _written = new List<byte>();
            private byte[] _scratch = [];

            #endregion

            #region Properties

            public int FlushCount { get; private set; }

            public IReadOnlyList<int> GetSpanRequests => _getSpanRequests;

            public byte[] WrittenBytes => _written.ToArray();

            #endregion

            #region Public Methods

            public override void Advance(int bytes)
            {
                for (int index = 0; index < bytes; index++)
                {
                    _written.Add(_scratch[index]);
                }
            }

            public override void CancelPendingFlush()
            {
            }

            public override void Complete(Exception? exception = null)
            {
            }

            public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
            {
                FlushCount++;

                return ValueTask.FromResult(new FlushResult(isCanceled: false, isCompleted: false));
            }

            public override Memory<byte> GetMemory(int sizeHint = 0)
            {
                return AllocateScratch(sizeHint);
            }

            public override Span<byte> GetSpan(int sizeHint = 0)
            {
                return AllocateScratch(sizeHint);
            }

            #endregion

            #region Private Methods

            private byte[] AllocateScratch(int sizeHint)
            {
                int size = sizeHint <= 0 ? 1024 : sizeHint;
                _getSpanRequests.Add(size);
                if (_scratch.Length < size)
                {
                    _scratch = new byte[size];
                }

                return _scratch;
            }

            #endregion

        }

    }

}
