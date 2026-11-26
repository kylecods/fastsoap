using System;
using System.Globalization;
using System.Threading.Tasks;

namespace FastSoap.Benchmarks.Benchmarks
{

    /// <summary>
    /// Deterministic steady-state allocation verification using <see cref="GC.GetAllocatedBytesForCurrentThread"/>
    /// across core FastSoap primitives and end-to-end dispatch paths.
    /// </summary>
    public static class AllocationVerification
    {

        #region Public Methods

        /// <summary>
        /// Executes warmup and steady-state <see cref="GC.GetAllocatedBytesForCurrentThread"/> measurements,
        /// printing exact per-operation byte allocations and throwing if expected zero-allocation paths allocate.
        /// </summary>
        /// <returns>Zero on success.</returns>
        public static async Task<int> RunAsync()
        {
            const int warmupIterations = 1000;
            const int measuredIterations = 10000;

            DispatchBenchmarks dispatch = new DispatchBenchmarks();
            dispatch.GlobalSetup();

            CoreComponentsBenchmarks components = new CoreComponentsBenchmarks();
            components.GlobalSetup();

            Console.WriteLine("=== FastSoap GC.GetAllocatedBytesForCurrentThread() Verification ===");
            Console.WriteLine($"Warmup iterations: {warmupIterations}, Measured iterations: {measuredIterations}");
            Console.WriteLine();
            Console.WriteLine("| Operation | Total Bytes (10k ops) | Bytes / Op | Zero-Alloc Verified |");
            Console.WriteLine("|---|---:|---:|:---:|");

            VerifySync("SoapOperationTable.TryGetByAction", warmupIterations, measuredIterations, expectZero: true, () => components.OperationTable_TryGetByAction_Transfer() ? 1 : 0);
            VerifySync("SoapOperationTable.TryGetByRequestElement", warmupIterations, measuredIterations, expectZero: true, () => components.OperationTable_TryGetByRequestElement_Transfer() ? 1 : 0);
            VerifySync("BoundedPooledBufferWriter (256 B rent/write/dispose)", warmupIterations, measuredIterations, expectZero: false, () => components.ResponseBuffer_RentWriteDispose_256B());
            VerifySync("SoapResponseWriter.WriteResponse (Echo, pooled)", warmupIterations, measuredIterations, expectZero: false, () => components.ResponseWriter_Echo_Soap11_Pooled());
            VerifySync("SoapResponseWriter.WriteResponse (Transfer, pooled)", warmupIterations, measuredIterations, expectZero: false, () => components.ResponseWriter_Transfer_Soap11_Pooled());
            VerifySync("SoapFaultWriter.WriteFault (SOAP 1.2, reusable)", warmupIterations, measuredIterations, expectZero: false, () => components.FaultWriter_Soap12_ReusableBuffer());
            VerifySync("SoapEnvelopeReader.ParseAndPrepare (Echo)", warmupIterations, measuredIterations, expectZero: false, () => components.EnvelopeReader_ParseAndPrepare_Echo());
            VerifySync("SoapEnvelopeReader.ParseAndPrepare (Transfer)", warmupIterations, measuredIterations, expectZero: false, () => components.EnvelopeReader_ParseAndPrepare_Transfer());

            await VerifyAsync("SoapRequestBuffer.CopyFromPipeAsync (Echo)", warmupIterations, measuredIterations, () => components.RequestBuffer_CopyFromPipe_Echo()).ConfigureAwait(false);
            await VerifyAsync("SoapMessageProcessor.ProcessAsync (Echo, Manual)", warmupIterations, measuredIterations, () => dispatch.ProcessAsync_Echo_Manual()).ConfigureAwait(false);
            await VerifyAsync("SoapMessageProcessor.ProcessAsync (Echo, Generated)", warmupIterations, measuredIterations, () => dispatch.ProcessAsync_Echo_Generated()).ConfigureAwait(false);
            await VerifyAsync("SoapMessageProcessor.ProcessAsync (Transfer, Manual)", warmupIterations, measuredIterations, () => dispatch.ProcessAsync_Transfer_Manual()).ConfigureAwait(false);
            await VerifyAsync("SoapMessageProcessor.ProcessAsync (Transfer, Generated)", warmupIterations, measuredIterations, () => dispatch.ProcessAsync_Transfer_Generated()).ConfigureAwait(false);

            return 0;
        }

        #endregion

        #region Private Methods

        private static void PrintRow(string name, long totalBytes, int iterations, bool expectZero)
        {
            double bytesPerOp = (double)totalBytes / iterations;
            string zeroStatus = totalBytes == 0 ? "Yes (0 B)" : $"No ({bytesPerOp.ToString("F1", CultureInfo.InvariantCulture)} B)";

            Console.WriteLine($"| {name} | {totalBytes.ToString("N0", CultureInfo.InvariantCulture)} | {bytesPerOp.ToString("F1", CultureInfo.InvariantCulture)} | {zeroStatus} |");

            if (expectZero && totalBytes > 0)
            {
                throw new InvalidOperationException($"Expected zero allocations for '{name}', but observed {totalBytes} bytes over {iterations} iterations.");
            }
        }

        private static async Task VerifyAsync(
            string name,
            int warmupIterations,
            int measuredIterations,
            Func<ValueTask<int>> action)
        {
            long sink = 0;
            for (int index = 0; index < warmupIterations; index++)
            {
                sink += await action().ConfigureAwait(false);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < measuredIterations; index++)
            {
                sink += await action().ConfigureAwait(false);
            }

            long after = GC.GetAllocatedBytesForCurrentThread();
            if (sink == long.MinValue)
            {
                Console.WriteLine(sink);
            }

            PrintRow(name, after - before, measuredIterations, expectZero: false);
        }

        private static void VerifySync(
            string name,
            int warmupIterations,
            int measuredIterations,
            bool expectZero,
            Func<int> action)
        {
            long sink = 0;
            for (int index = 0; index < warmupIterations; index++)
            {
                sink += action();
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < measuredIterations; index++)
            {
                sink += action();
            }

            long after = GC.GetAllocatedBytesForCurrentThread();
            if (sink == long.MinValue)
            {
                Console.WriteLine(sink);
            }

            PrintRow(name, after - before, measuredIterations, expectZero);
        }

        #endregion

    }

}
