using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

using FastSoap.Benchmarks.Services;

namespace FastSoap.Benchmarks.HttpLoad
{

    /// <summary>
    /// Real Kestrel loopback HTTP comparative load harness executing warmup + steady-state runs at concurrency
    /// 1 and 16 on identical Echo and Transfer SOAP 1.1 payloads across FastSoap, CoreWCF 1.9.2, and SoapCore 1.2.1.18.
    /// </summary>
    public static class HttpLoadRunner
    {

        #region Fields

        private static readonly MediaTypeHeaderValue Soap11MediaType = MediaTypeHeaderValue.Parse("text/xml; charset=utf-8");

        #endregion

        #region Public Methods

        /// <summary>
        /// Executes the full comparative HTTP benchmark matrix across all server variants in isolated child server processes.
        /// </summary>
        /// <returns>Zero if all scenarios complete with zero errors; non-zero otherwise.</returns>
        public static async Task<int> RunAsync()
        {
            string[] variants =
            [
                "fastsoap-manual",
                "fastsoap-generated",
                "corewcf-dcs",
                "corewcf-xmlser",
                "soapcore-xmlser"
            ];

            ScenarioSpec[] scenarios =
            [
                new ScenarioSpec("Echo", "/echo.svc", BenchmarkPayloads.QuotedEchoAction, BenchmarkPayloads.EchoSoap11RequestUtf8, "hello-fastsoap", 1),
                new ScenarioSpec("Echo", "/echo.svc", BenchmarkPayloads.QuotedEchoAction, BenchmarkPayloads.EchoSoap11RequestUtf8, "hello-fastsoap", 16),
                new ScenarioSpec("Transfer", "/payments.svc", BenchmarkPayloads.QuotedTransferAction, BenchmarkPayloads.TransferSoap11RequestUtf8, "TX-001", 1),
                new ScenarioSpec("Transfer", "/payments.svc", BenchmarkPayloads.QuotedTransferAction, BenchmarkPayloads.TransferSoap11RequestUtf8, "TX-001", 16)
            ];

            TimeSpan warmupDuration = TimeSpan.FromSeconds(3);
            TimeSpan measureDuration = TimeSpan.FromSeconds(8);

            List<HttpLoadMeasurement> allResults = new List<HttpLoadMeasurement>(variants.Length * scenarios.Length);

            Console.WriteLine("=== FastSoap Comparative Kestrel HTTP Load Benchmark (JIT net10.0 Release, Production, Logging Cleared) ===");
            Console.WriteLine($"Runtime: {Environment.Version} | OS: {Environment.OSVersion} | Logical CPUs: {Environment.ProcessorCount}");
            Console.WriteLine($"Warmup: {warmupDuration.TotalSeconds:F0}s | Steady-State Window: {measureDuration.TotalSeconds:F0}s | Loopback HTTP/1.1 Cleartext");
            Console.WriteLine();

            foreach (string variant in variants)
            {
                Console.WriteLine($"--- Starting isolated server: {variant} ---");
                await using IsolatedServerProcess server = await IsolatedServerProcess.StartAsync(variant).ConfigureAwait(false);

                foreach (string diagnostic in server.CoreWcfDiagnostics)
                {
                    Console.WriteLine($"  {diagnostic}");
                }

                using SocketsHttpHandler handler = new SocketsHttpHandler
                {
                    PooledConnectionLifetime = Timeout.InfiniteTimeSpan,
                    MaxConnectionsPerServer = 64,
                    AllowAutoRedirect = false,
                    UseProxy = false
                };

                using HttpClient client = new HttpClient(handler)
                {
                    BaseAddress = server.BaseUri,
                    DefaultRequestVersion = HttpVersion.Version11,
                    DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
                    Timeout = TimeSpan.FromSeconds(15)
                };

                // Verify exact wire response once per endpoint before load runs
                await VerifyEndpointAsync(client, "/echo.svc", BenchmarkPayloads.QuotedEchoAction, BenchmarkPayloads.EchoSoap11RequestUtf8, "hello-fastsoap", variant).ConfigureAwait(false);
                await VerifyEndpointAsync(client, "/payments.svc", BenchmarkPayloads.QuotedTransferAction, BenchmarkPayloads.TransferSoap11RequestUtf8, "TX-001", variant).ConfigureAwait(false);

                foreach (ScenarioSpec scenario in scenarios)
                {
                    // Warmup window (not measured)
                    _ = await ExecuteLoadWindowAsync(client, scenario, warmupDuration, recordLatencies: false).ConfigureAwait(false);

                    ServerGcStats beforeStats = await ReadServerStatsAsync(client).ConfigureAwait(false);
                    LoadWindowResult window = await ExecuteLoadWindowAsync(client, scenario, measureDuration, recordLatencies: true).ConfigureAwait(false);
                    ServerGcStats afterStats = await ReadServerStatsAsync(client).ConfigureAwait(false);

                    long allocatedDelta = afterStats.TotalAllocatedBytes - beforeStats.TotalAllocatedBytes;
                    int gen0Delta = afterStats.Gen0 - beforeStats.Gen0;
                    int gen1Delta = afterStats.Gen1 - beforeStats.Gen1;
                    int gen2Delta = afterStats.Gen2 - beforeStats.Gen2;
                    double rps = window.TotalRequests / window.ElapsedSeconds;
                    double allocPerReq = window.TotalRequests > 0 ? (double)allocatedDelta / window.TotalRequests : 0d;

                    HttpLoadMeasurement measurement = new HttpLoadMeasurement(
                        variant,
                        scenario.PayloadName,
                        scenario.Concurrency,
                        window.TotalRequests,
                        window.ErrorCount,
                        rps,
                        window.P50Ms,
                        window.P95Ms,
                        window.P99Ms,
                        allocPerReq,
                        gen0Delta,
                        gen1Delta,
                        gen2Delta,
                        afterStats.WorkingSetBytes);

                    allResults.Add(measurement);

                    Console.WriteLine(
                        $"  {variant,-18} | {scenario.PayloadName,-8} | c={scenario.Concurrency,2} | " +
                        $"Reqs={window.TotalRequests,7} | Err={window.ErrorCount} | RPS={rps,9:F1} | " +
                        $"p50={window.P50Ms,6:F3}ms p95={window.P95Ms,6:F3}ms p99={window.P99Ms,6:F3}ms | " +
                        $"ServerAlloc={allocPerReq,8:F0} B/req | GC={gen0Delta}/{gen1Delta}/{gen2Delta}");

                    if (window.ErrorCount > 0)
                    {
                        throw new InvalidOperationException(
                            $"Observed {window.ErrorCount} HTTP/wire errors on {variant} {scenario.PayloadName} c={scenario.Concurrency}.");
                    }
                }

                Console.WriteLine();
            }

            Console.WriteLine("=== Verbatim Markdown Summary Table ===");
            Console.WriteLine("| Server | Operation | Concurrency | Requests | Errors | RPS | p50 (ms) | p95 (ms) | p99 (ms) | Server Alloc (B/req) | Server GC (G0/G1/G2) |");
            Console.WriteLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|");

            foreach (HttpLoadMeasurement m in allResults)
            {
                Console.WriteLine(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"| {m.Variant} | {m.Operation} | {m.Concurrency} | {m.Requests} | {m.Errors} | {m.Rps:F1} | {m.P50Ms:F3} | {m.P95Ms:F3} | {m.P99Ms:F3} | {m.ServerAllocBytesPerRequest:F0} | {m.Gen0}/{m.Gen1}/{m.Gen2} |"));
            }

            return 0;
        }

        #endregion

        #region Private Methods

        private static async Task<LoadWindowResult> ExecuteLoadWindowAsync(
            HttpClient client,
            ScenarioSpec scenario,
            TimeSpan duration,
            bool recordLatencies)
        {
            using CancellationTokenSource cts = new CancellationTokenSource(duration);
            CancellationToken ct = cts.Token;

            long totalRequests = 0;
            long errorCount = 0;
            List<double>[] perWorkerLatencies = new List<double>[scenario.Concurrency];
            if (recordLatencies)
            {
                for (int index = 0; index < perWorkerLatencies.Length; index++)
                {
                    perWorkerLatencies[index] = new List<double>(8192);
                }
            }

            long startTimestamp = Stopwatch.GetTimestamp();
            Task[] workers = new Task[scenario.Concurrency];

            for (int workerIndex = 0; workerIndex < scenario.Concurrency; workerIndex++)
            {
                int capturedIndex = workerIndex;
                workers[workerIndex] = Task.Run(async () =>
                {
                    List<double>? latencies = recordLatencies ? perWorkerLatencies[capturedIndex] : null;

                    while (!ct.IsCancellationRequested)
                    {
                        long reqStart = Stopwatch.GetTimestamp();
                        try
                        {
                            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, scenario.Path);
                            request.Headers.TryAddWithoutValidation("SOAPAction", scenario.QuotedSoapAction);
                            ByteArrayContent content = new ByteArrayContent(scenario.RequestBodyUtf8);
                            content.Headers.ContentType = Soap11MediaType;
                            request.Content = content;

                            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
                            byte[] responseBytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);

                            if (response.StatusCode != HttpStatusCode.OK || responseBytes.Length == 0)
                            {
                                Interlocked.Increment(ref errorCount);
                            }
                            else
                            {
                                Interlocked.Increment(ref totalRequests);
                                if (latencies is not null)
                                {
                                    latencies.Add(Stopwatch.GetElapsedTime(reqStart).TotalMilliseconds);
                                }
                            }
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            break;
                        }
                        catch
                        {
                            Interlocked.Increment(ref errorCount);
                        }
                    }
                });
            }

            await Task.WhenAll(workers).ConfigureAwait(false);
            double elapsedSeconds = Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds;

            if (!recordLatencies)
            {
                return new LoadWindowResult(totalRequests, errorCount, elapsedSeconds, 0, 0, 0);
            }

            int totalSamples = 0;
            for (int index = 0; index < perWorkerLatencies.Length; index++)
            {
                totalSamples += perWorkerLatencies[index].Count;
            }

            List<double> merged = new List<double>(totalSamples);
            for (int index = 0; index < perWorkerLatencies.Length; index++)
            {
                merged.AddRange(perWorkerLatencies[index]);
            }

            merged.Sort();

            return new LoadWindowResult(
                totalRequests,
                errorCount,
                elapsedSeconds,
                Percentile(merged, 0.50),
                Percentile(merged, 0.95),
                Percentile(merged, 0.99));
        }

        private static double Percentile(List<double> sorted, double quantile)
        {
            if (sorted.Count == 0)
            {
                return 0d;
            }

            int index = (int)Math.Ceiling(quantile * sorted.Count) - 1;
            index = Math.Clamp(index, 0, sorted.Count - 1);

            return sorted[index];
        }

        private static async Task<ServerGcStats> ReadServerStatsAsync(HttpClient client)
        {
            string raw = await client.GetStringAsync("/__bench/stats").ConfigureAwait(false);
            string[] parts = raw.Split('|');
            if (parts.Length != 5)
            {
                throw new InvalidOperationException($"Unexpected stats payload '{raw}'.");
            }

            return new ServerGcStats(
                long.Parse(parts[0], CultureInfo.InvariantCulture),
                int.Parse(parts[1], CultureInfo.InvariantCulture),
                int.Parse(parts[2], CultureInfo.InvariantCulture),
                int.Parse(parts[3], CultureInfo.InvariantCulture),
                long.Parse(parts[4], CultureInfo.InvariantCulture));
        }

        private static async Task VerifyEndpointAsync(
            HttpClient client,
            string path,
            string quotedSoapAction,
            byte[] requestBytes,
            string expectedToken,
            string variant)
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, path);
            request.Headers.TryAddWithoutValidation("SOAPAction", quotedSoapAction);
            ByteArrayContent content = new ByteArrayContent(requestBytes);
            content.Headers.ContentType = Soap11MediaType;
            request.Content = content;

            using HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.OK || !body.Contains(expectedToken, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Verification failed for {variant} {path}: HTTP {(int)response.StatusCode} {body}");
            }
        }

        #endregion

        #region Nested Types

        private sealed record HttpLoadMeasurement(
            string Variant,
            string Operation,
            int Concurrency,
            long Requests,
            long Errors,
            double Rps,
            double P50Ms,
            double P95Ms,
            double P99Ms,
            double ServerAllocBytesPerRequest,
            int Gen0,
            int Gen1,
            int Gen2,
            long WorkingSetBytes);

        private sealed class IsolatedServerProcess : IAsyncDisposable
        {

            #region Fields

            private readonly Process _process;

            #endregion

            #region Properties

            public Uri BaseUri { get; }

            public IReadOnlyList<string> CoreWcfDiagnostics { get; }

            #endregion

            #region Constructors

            private IsolatedServerProcess(Process process, Uri baseUri, IReadOnlyList<string> coreWcfDiagnostics)
            {
                _process = process;
                BaseUri = baseUri;
                CoreWcfDiagnostics = coreWcfDiagnostics;
            }

            #endregion

            #region Public Methods

            public async ValueTask DisposeAsync()
            {
                try
                {
                    if (!_process.HasExited)
                    {
                        _process.Kill(entireProcessTree: true);
                        await _process.WaitForExitAsync().ConfigureAwait(false);
                    }
                }
                catch
                {
                    // Best-effort cleanup during shutdown.
                }
                finally
                {
                    _process.Dispose();
                }
            }

            public static async Task<IsolatedServerProcess> StartAsync(string variant)
            {
                string? processPath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(processPath))
                {
                    throw new InvalidOperationException("Cannot resolve current executable path.");
                }

                string entryAssembly = typeof(HttpLoadRunner).Assembly.Location;
                bool isDotnetHost = processPath.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase) ||
                    processPath.EndsWith("dotnet", StringComparison.OrdinalIgnoreCase);

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = processPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                if (isDotnetHost)
                {
                    startInfo.ArgumentList.Add(entryAssembly);
                }

                startInfo.ArgumentList.Add("--serve");
                startInfo.ArgumentList.Add(variant);

                Process process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException($"Failed to start server process for '{variant}'.");

                List<string> diagnostics = [];
                Uri? readyUri = null;

                using CancellationTokenSource timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

                while (!timeoutCts.IsCancellationRequested)
                {
                    string? line = await process.StandardOutput.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false);
                    if (line is null)
                    {
                        string stderr = await process.StandardError.ReadToEndAsync(timeoutCts.Token).ConfigureAwait(false);
                        process.Dispose();

                        throw new InvalidOperationException($"Server '{variant}' exited prematurely. StdErr: {stderr}");
                    }

                    if (line.StartsWith("COREWCF_OP|", StringComparison.Ordinal))
                    {
                        diagnostics.Add(line);
                        continue;
                    }

                    if (line.StartsWith("READY|", StringComparison.Ordinal))
                    {
                        string[] parts = line.Split('|');
                        if (parts.Length >= 3)
                        {
                            readyUri = new Uri(parts[2], UriKind.Absolute);
                            break;
                        }
                    }
                }

                if (readyUri is null)
                {
                    process.Kill(entireProcessTree: true);
                    process.Dispose();

                    throw new TimeoutException($"Timed out waiting for server '{variant}' to report READY.");
                }

                return new IsolatedServerProcess(process, readyUri, diagnostics);
            }

            #endregion

        }

        private readonly record struct LoadWindowResult(
            long TotalRequests,
            long ErrorCount,
            double ElapsedSeconds,
            double P50Ms,
            double P95Ms,
            double P99Ms);

        private sealed record ScenarioSpec(
            string PayloadName,
            string Path,
            string QuotedSoapAction,
            byte[] RequestBodyUtf8,
            string ExpectedToken,
            int Concurrency);

        private readonly record struct ServerGcStats(
            long TotalAllocatedBytes,
            int Gen0,
            int Gen1,
            int Gen2,
            long WorkingSetBytes);

        #endregion

    }

}
