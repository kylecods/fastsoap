using System;
using System.Threading.Tasks;

using BenchmarkDotNet.Running;

using FastSoap.Benchmarks.Benchmarks;
using FastSoap.Benchmarks.HttpLoad;

namespace FastSoap.Benchmarks
{

    /// <summary>
    /// Entry point for the FastSoap comparative benchmark suite supporting BenchmarkDotNet microbenchmarks,
    /// deterministic <see cref="GC.GetAllocatedBytesForCurrentThread"/> checks, and Kestrel HTTP load tests.
    /// </summary>
    public static class Program
    {

        #region Public Methods

        /// <summary>
        /// Dispatches execution to HTTP load mode (<c>--http-load</c>), isolated server mode (<c>--serve &lt;variant&gt;</c>),
        /// allocation verification (<c>--verify-allocs</c>), or BenchmarkDotNet microbenchmarks.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Zero on success; non-zero on failure.</returns>
        public static async Task<int> Main(string[] args)
        {
            if (args.Length >= 1 && string.Equals(args[0], "--http-load", StringComparison.OrdinalIgnoreCase))
            {
                return await HttpLoadRunner.RunAsync().ConfigureAwait(false);
            }

            if (args.Length >= 2 && string.Equals(args[0], "--serve", StringComparison.OrdinalIgnoreCase))
            {
                return await ServerHost.RunServerAsync(args[1]).ConfigureAwait(false);
            }

            if (args.Length >= 1 && string.Equals(args[0], "--verify-allocs", StringComparison.OrdinalIgnoreCase))
            {
                return await AllocationVerification.RunAsync().ConfigureAwait(false);
            }

            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

            return 0;
        }

        #endregion

    }

}
