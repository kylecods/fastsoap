# FastSoap Comparative Performance Report (H1 + H2)

**Report Date:** 2026-11-26  
**Work Packages:** H1 + H2 (Reproducible Microbenchmarks & Comparative Kestrel HTTP Baselines)  
**Benchmark Harness:** [benchmarks/FastSoap.Benchmarks/](../benchmarks/FastSoap.Benchmarks/)  
**Status:** Executed on JIT `net10.0` (`10.0.15`) Release; **no Native AOT binary benchmarked** (see transparency notice below).

> **Native AOT Transparency Notice:**  
> Publishing and running a linked Native AOT (`PublishAot=true`) executable on this Windows host requires MSVC C++ build tools (`link.exe`) and Windows SDK native libraries (or Linux `clang` + `zlib1g-dev`), which are **not installed** on this machine (verified in [ADR-001](adr/ADR-001-runtime-architecture.md) §3.3 and [spike-results.md](research/spike-results.md) §1). Consequently, **100% of the measurements below are JIT `net10.0` (`10.0.15`) Release builds**. No Native AOT throughput, latency, or allocation numbers are claimed or extrapolated here.

---

## 1. Host Environment, Toolchain & Pinned Versions

All measurements were collected sequentially on a single Windows host in `Release` configuration (`-c Release`) with the `High performance` Windows power plan active (`8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c`).

| Component | Observed Value |
|---|---|
| **Machine Model** | HP ProBook 450 G7 |
| **OS** | Microsoft Windows 11 / Windows NT `10.0.22631.6199` (`win-x64`, Build `22631`) |
| **CPU** | `Intel(R) Core(TM) i5-10210U CPU @ 1.60GHz` (Max: `2.11 GHz`, 1 socket, 4 physical cores, 8 logical processors; `X64 RyuJIT x86-64-v3`, `AVX2`) |
| **Physical Memory** | `17,009,516,544` bytes (~15.84 GiB) |
| **.NET SDK (`global.json`)** | `11.0.100` (commit `297acf930f`, MSBuild `18.12.2-1.26529.118+297acf930f`) |
| **Target Framework & Runtime** | `net10.0` — `Microsoft.NETCore.App` & `Microsoft.AspNetCore.App` `10.0.15` (`10.0.1526.53008`), C# 15 (`<LangVersion>15</LangVersion>`) |
| **FastSoap XML Backend** | `XmlEssentials` `1.0.0` (source-generated `BenchmarkXmlContext`, `IsReflectionEnabledByDefault=false`) |
| **FastSoap Source Generator** | `FastSoap.Generators` (project analyzer generating `BenchmarkEchoServiceSoapContract`, `BenchmarkPaymentServiceSoapContract`, `MapBenchmarkEchoService`, `MapBenchmarkPaymentService`) |
| **CoreWCF Baseline** | `CoreWCF.Http` `1.9.2` + `CoreWCF.Primitives` `1.9.2` (`EnableCoreWCFOperationInvokerGenerator=true`, `EnableCoreWCFParameterInjectionGenerator=true`) |
| **SoapCore Baseline** | `SoapCore` `1.2.1.18` (`SoapSerializer.XmlSerializer`, `OmitXmlDeclaration=true`, `IndentXml=false`) |
| **Microbenchmark Harness** | `BenchmarkDotNet` `0.15.8` (`[MemoryDiagnoser]`, `[ShortRunJob]`) |

---

## 2. Wire Contracts & Fairness Configuration

All benchmarks execute on identical SOAP 1.1 `text/xml; charset=utf-8` document/literal wire contracts matching the canonical R0-G compatibility corpus ([tests/fixtures/soap11/](../tests/fixtures/soap11/)):

1. **Echo (`IEchoService`, `http://tempuri.org/`):**
   - `SOAPAction`: `"http://tempuri.org/IEchoService/EchoString"`
   - Payload (`169` UTF-8 bytes): `<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><EchoString xmlns="http://tempuri.org/"><echo>hello-fastsoap</echo></EchoString></s:Body></s:Envelope>`
   - Response: `<EchoStringResponse xmlns="http://tempuri.org/"><EchoStringResult>hello-fastsoap</EchoStringResult></EchoStringResponse>`
2. **Nested Transfer (`IPaymentService`, `urn:payments`):**
   - `SOAPAction`: `"urn:payments/IPaymentService/Transfer"`
   - Payload (`349` UTF-8 bytes): `<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><Transfer xmlns="urn:payments"><request><AccountNumber>123456</AccountNumber><Amount>100.50</Amount><Currency>USD</Currency><Metadata><CorrelationId>CORR-2026-0001</CorrelationId><Reference>INV-42</Reference></Metadata><Note>Invoice settlement</Note></request></Transfer></s:Body></s:Envelope>`
   - Response: deterministic `TransferResult` (`TransactionId="TX-001"`, `Successful=true`, `ProcessedAtUtc=2026-11-26T12:00:00Z`, `Status=Completed`).

### HTTP Load Fairness Controls (`--http-load`)
- Every server runs in an **isolated child process** (`--serve <variant>`) in `Environments.Production` with `builder.Logging.ClearProviders()` and real Kestrel loopback TCP (`127.0.0.1:0`, HTTP/1.1 cleartext).
- FastSoap hosts via `WebApplication.CreateSlimBuilder` (its standard hosting path); CoreWCF and SoapCore host via `WebApplication.CreateBuilder` with `Logging.ClearProviders()`.
- CoreWCF services use `BasicHttpBinding(BasicHttpSecurityMode.None)` with `[ServiceBehavior(InstanceContextMode = InstanceContextMode.Single, ConcurrencyMode = ConcurrencyMode.Multiple)]` so concurrent requests at `c=16` are never artificially serialized by `ConcurrencyMode.Single`.
- Server-side allocation bytes/request and GC counts (`Gen0`/`Gen1`/`Gen2`) are sampled exclusively inside the server process via `GET /__bench/stats` immediately before and after each steady-state measurement window, isolating server memory from `HttpClient` load-driver allocations.
- All endpoints are verified for HTTP `200` and expected response payload tokens before load begins, and every steady-state request asserts `HTTP 200` and non-empty body (`Errors` must be `0`).

---

## 3. Exact Commands Executed

```powershell
dotnet build benchmarks/FastSoap.Benchmarks/FastSoap.Benchmarks.csproj -c Release --no-restore
dotnet run --project benchmarks/FastSoap.Benchmarks/FastSoap.Benchmarks.csproj -c Release --no-build -- --filter * --artifacts benchmarks/FastSoap.Benchmarks/BenchmarkDotNet.Artifacts
dotnet run --project benchmarks/FastSoap.Benchmarks/FastSoap.Benchmarks.csproj -c Release --no-build -- --verify-allocs
dotnet run --project benchmarks/FastSoap.Benchmarks/FastSoap.Benchmarks.csproj -c Release --no-build -- --http-load
```

---

## 4. BenchmarkDotNet Microbenchmarks (`FastSoap.Core`)

### 4.1 End-to-End In-Process Dispatch (`DispatchBenchmarks`)

Verbatim output from `FastSoap.Benchmarks.Benchmarks.DispatchBenchmarks-report-github.md`:

```text
BenchmarkDotNet v0.15.8, Windows 11 (10.0.22631.6199/23H2/2023Update/SunValley3)
Intel Core i5-10210U CPU 1.60GHz (Max: 2.11GHz), 1 CPU, 8 logical and 4 physical cores
.NET SDK 11.0.100
  [Host]   : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  
```

| Method                          | Mean     | Error    | StdDev    | Gen0   | Allocated |
|-------------------------------- |---------:|---------:|----------:|-------:|----------:|
| ProcessAsync_Echo_Generated     | 2.786 μs | 4.753 μs | 0.2605 μs | 0.6866 |   2.11 KB |
| ProcessAsync_Echo_Manual        | 2.812 μs | 7.098 μs | 0.3890 μs | 0.6638 |   2.04 KB |
| ProcessAsync_Transfer_Generated | 4.499 μs | 5.003 μs | 0.2742 μs | 0.7782 |   2.39 KB |
| ProcessAsync_Transfer_Manual    | 4.986 μs | 4.003 μs | 0.2194 μs | 0.7553 |   2.32 KB |

### 4.2 Core Protocol, Routing, Pooling & Serialization (`CoreComponentsBenchmarks`)

Verbatim output from `FastSoap.Benchmarks.Benchmarks.CoreComponentsBenchmarks-report-github.md`:

```text
BenchmarkDotNet v0.15.8, Windows 11 (10.0.22631.6199/23H2/2023Update/SunValley3)
Intel Core i5-10210U CPU 1.60GHz (Max: 2.11GHz), 1 CPU, 8 logical and 4 physical cores
.NET SDK 11.0.100
  [Host]   : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  
```

| Method                                         | Mean         | Error        | StdDev      | Gen0   | Allocated |
|----------------------------------------------- |-------------:|-------------:|------------:|-------:|----------:|
| ContentTypeAndActionParser_Soap11              |   272.485 ns | 1,730.839 ns |  94.8731 ns | 0.0405 |     128 B |
| EnvelopeReader_ParseAndPrepare_Echo            | 1,240.937 ns | 4,326.862 ns | 237.1699 ns | 0.0734 |     232 B |
| EnvelopeReader_ParseAndPrepare_Transfer        | 1,764.266 ns |   709.313 ns |  38.8798 ns | 0.1602 |     504 B |
| FaultWriter_Soap11_Pooled                      |   634.496 ns |   576.051 ns |  31.5753 ns | 0.5150 |    1616 B |
| FaultWriter_Soap12_ReusableBuffer              |   958.839 ns |   720.245 ns |  39.4791 ns | 0.4959 |    1560 B |
| OperationTable_TryGetByAction_Transfer         |     7.078 ns |    15.693 ns |   0.8602 ns |      - |         - |
| OperationTable_TryGetByRequestElement_Transfer |     5.435 ns |    18.854 ns |   1.0335 ns |      - |         - |
| RequestBuffer_CopyFromPipe_Echo                |   487.904 ns |   741.023 ns |  40.6180 ns | 0.0124 |      40 B |
| ResponseBuffer_RentWriteDispose_256B           |    46.981 ns |    55.832 ns |   3.0603 ns | 0.0153 |      48 B |
| ResponseBuffer_RentWriteDispose_2KiB           |    87.376 ns |   174.419 ns |   9.5605 ns | 0.0153 |      48 B |
| ResponseWriter_Echo_Soap11_Pooled              |   985.118 ns | 1,435.193 ns |  78.6678 ns | 0.5169 |    1624 B |
| ResponseWriter_Transfer_Soap11_Pooled          | 1,545.711 ns | 3,377.124 ns | 185.1116 ns | 0.5150 |    1616 B |

### 4.3 Direct `GC.GetAllocatedBytesForCurrentThread()` Verification (`--verify-allocs`)

Verbatim output (1,000 warmup iterations, 10,000 measured steady-state iterations):

| Operation | Total Bytes (10k ops) | Bytes / Op | Zero-Alloc Verified |
|---|---:|---:|:---:|
| SoapOperationTable.TryGetByAction | 0 | 0.0 | Yes (0 B) |
| SoapOperationTable.TryGetByRequestElement | 0 | 0.0 | Yes (0 B) |
| BoundedPooledBufferWriter (256 B rent/write/dispose) | 480,000 | 48.0 | No (48.0 B) |
| SoapResponseWriter.WriteResponse (Echo, pooled) | 16,241,200 | 1624.1 | No (1624.1 B) |
| SoapResponseWriter.WriteResponse (Transfer, pooled) | 16,161,200 | 1616.1 | No (1616.1 B) |
| SoapFaultWriter.WriteFault (SOAP 1.2, reusable) | 15,600,000 | 1560.0 | No (1560.0 B) |
| SoapEnvelopeReader.ParseAndPrepare (Echo) | 2,320,000 | 232.0 | No (232.0 B) |
| SoapEnvelopeReader.ParseAndPrepare (Transfer) | 5,040,240 | 504.0 | No (504.0 B) |
| SoapRequestBuffer.CopyFromPipeAsync (Echo) | 400,000 | 40.0 | No (40.0 B) |
| SoapMessageProcessor.ProcessAsync (Echo, Manual) | 20,881,680 | 2088.2 | No (2088.2 B) |
| SoapMessageProcessor.ProcessAsync (Echo, Generated) | 21,601,680 | 2160.2 | No (2160.2 B) |
| SoapMessageProcessor.ProcessAsync (Transfer, Manual) | 23,761,920 | 2376.2 | No (2376.2 B) |
| SoapMessageProcessor.ProcessAsync (Transfer, Generated) | 24,481,920 | 2448.2 | No (2448.2 B) |

---

## 5. Kestrel Loopback HTTP Comparative Load Results (`--http-load`)

### 5.1 CoreWCF Generated Operation Invoker Verification

Printed verbatim by the isolated `corewcf-dcs` and `corewcf-xmlser` server processes at startup, confirming `CoreWCF.BuildTools` source-generated invokers were active for every operation:

```text
COREWCF_OP|/echo.svc|EchoString|CoreWCF.Dispatcher.<OperationInvoker_g>F432B0F96D733C854A0DB93D7ACD2090E173DAAFF9C78037E27C2FC705BEA5947__OperationInvoker0|CoreWCF.Dispatcher.PrimitiveOperationFormatter
COREWCF_OP|/payments.svc|Transfer|CoreWCF.Dispatcher.<OperationInvoker_g>F432B0F96D733C854A0DB93D7ACD2090E173DAAFF9C78037E27C2FC705BEA5947__OperationInvoker2|CoreWCF.Dispatcher.DataContractSerializerOperationFormatter
COREWCF_OP|/echo.svc|EchoString|CoreWCF.Dispatcher.<OperationInvoker_g>F432B0F96D733C854A0DB93D7ACD2090E173DAAFF9C78037E27C2FC705BEA5947__OperationInvoker1|CoreWCF.Dispatcher.XmlSerializerOperationFormatter
COREWCF_OP|/payments.svc|Transfer|CoreWCF.Dispatcher.<OperationInvoker_g>F432B0F96D733C854A0DB93D7ACD2090E173DAAFF9C78037E27C2FC705BEA5947__OperationInvoker3|CoreWCF.Dispatcher.XmlSerializerOperationFormatter
```

### 5.2 Comparative Throughput, Latency & Server Allocation Table

Run parameters: `Warmup: 3s | Steady-State Window: 8s | Loopback HTTP/1.1 Cleartext | Production | Logging Cleared`.

Verbatim output table from `--http-load`:

| Server | Operation | Concurrency | Requests | Errors | RPS | p50 (ms) | p95 (ms) | p99 (ms) | Server Alloc (B/req) | Server GC (G0/G1/G2) |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| fastsoap-manual | Echo | 1 | 2360 | 0 | 294.6 | 3.326 | 4.417 | 4.841 | 2981 | 2/1/0 |
| fastsoap-manual | Echo | 16 | 10857 | 0 | 1352.7 | 10.874 | 19.762 | 31.477 | 2987 | 10/1/1 |
| fastsoap-manual | Transfer | 1 | 2338 | 0 | 291.8 | 3.318 | 4.527 | 4.933 | 3255 | 3/0/0 |
| fastsoap-manual | Transfer | 16 | 10575 | 0 | 1320.1 | 11.258 | 20.425 | 31.328 | 3267 | 11/1/0 |
| fastsoap-generated | Echo | 1 | 2356 | 0 | 294.3 | 3.343 | 4.392 | 4.855 | 3055 | 2/1/0 |
| fastsoap-generated | Echo | 16 | 11153 | 0 | 1391.9 | 10.755 | 18.372 | 27.673 | 3058 | 11/1/1 |
| fastsoap-generated | Transfer | 1 | 2431 | 0 | 303.5 | 3.110 | 4.287 | 4.661 | 3325 | 3/0/0 |
| fastsoap-generated | Transfer | 16 | 10803 | 0 | 1347.7 | 11.174 | 19.243 | 26.791 | 3337 | 12/0/0 |
| corewcf-dcs | Echo | 1 | 2269 | 0 | 283.0 | 3.469 | 4.558 | 5.030 | 7024 | 5/0/0 |
| corewcf-dcs | Echo | 16 | 9362 | 0 | 1165.4 | 12.335 | 25.094 | 38.849 | 7004 | 16/1/0 |
| corewcf-dcs | Transfer | 1 | 2326 | 0 | 290.3 | 3.264 | 4.510 | 5.012 | 8193 | 7/5/1 |
| corewcf-dcs | Transfer | 16 | 10444 | 0 | 1299.0 | 11.546 | 19.683 | 28.162 | 8163 | 26/23/0 |
| corewcf-xmlser | Echo | 1 | 2296 | 0 | 286.9 | 3.440 | 4.447 | 4.789 | 11648 | 8/0/0 |
| corewcf-xmlser | Echo | 16 | 10447 | 0 | 1299.5 | 11.441 | 20.013 | 29.722 | 11626 | 36/7/1 |
| corewcf-xmlser | Transfer | 1 | 2328 | 0 | 290.9 | 3.250 | 4.452 | 4.984 | 13731 | 12/11/1 |
| corewcf-xmlser | Transfer | 16 | 10486 | 0 | 1304.3 | 11.600 | 19.008 | 25.884 | 13684 | 45/33/0 |
| soapcore-xmlser | Echo | 1 | 2131 | 0 | 266.2 | 3.706 | 4.692 | 5.145 | 93162 | 63/1/1 |
| soapcore-xmlser | Echo | 16 | 10251 | 0 | 1279.1 | 11.804 | 19.254 | 26.555 | 93134 | 219/109/0 |
| soapcore-xmlser | Transfer | 1 | 2285 | 0 | 285.6 | 3.309 | 4.536 | 4.947 | 97268 | 71/1/0 |
| soapcore-xmlser | Transfer | 16 | 9732 | 0 | 1212.9 | 12.382 | 20.916 | 29.574 | 97478 | 216/107/1 |

---

## 6. Observed Analysis & Honest Findings

1. **Server Allocation & GC Pressure (Largest Differentiator):**
   - On **Echo (`c=16`)**, `fastsoap-manual` allocated **`2,987 B/req`** (`10/1/1` GCs) and `fastsoap-generated` allocated **`3,058 B/req`** (`11/1/1` GCs):
     - **57.4% fewer bytes/req** than `corewcf-dcs` (`7,004 B/req`, `2.34x`).
     - **74.3% fewer bytes/req** than `corewcf-xmlser` (`11,626 B/req`, `3.89x`).
     - **96.8% fewer bytes/req** than `soapcore-xmlser` (`93,134 B/req`, `31.18x`, `219` Gen0 + `109` Gen1 GCs in 8 seconds).
   - On **Transfer (`c=16`)**, `fastsoap-manual` allocated **`3,267 B/req`** (`11/1/0` GCs) and `fastsoap-generated` allocated **`3,337 B/req`** (`12/0/0` GCs):
     - **60.0% fewer bytes/req** than `corewcf-dcs` (`8,163 B/req`, `2.50x`, `26` Gen0 + `23` Gen1 GCs).
     - **76.1% fewer bytes/req** than `corewcf-xmlser` (`13,684 B/req`, `4.19x`, `45` Gen0 + `33` Gen1 GCs).
     - **96.6% fewer bytes/req** than `soapcore-xmlser` (`97,478 B/req`, `29.84x`, `216` Gen0 + `107` Gen1 + `1` Gen2 GCs).
   - Note where `FastSoap.Core` currently allocates: `OperationTable` routing (`TryGetByAction` / `TryGetByRequestElement`) is **verified `0.0 B`**. `SoapRequestBuffer` allocates `40.0 B` (`SoapRequestBuffer` class wrapper; `ArrayPool<byte>` rental is pooled) and `BoundedPooledBufferWriter` allocates `48.0 B` (wrapper object). The remaining ~1.56–1.62 KB inside `SoapResponseWriter` / `SoapFaultWriter` comes from constructing a new `Utf8XmlWriter` + internal writer state per response, and ~232–504 B in `SoapEnvelopeReader` comes from `PreparedSoapCall` / `SoapCallContext` / `string` action/DTO instances.
2. **In-Process CPU vs Single-Host Loopback HTTP Throughput:**
   - In-process, `FastSoap.Core` executes end-to-end `ProcessAsync` in **`2.786–2.812 µs`** for `Echo` and **`4.499–4.986 µs`** for `Transfer`.
   - On single-host Windows 11 loopback HTTP/1.1 (`127.0.0.1`) with client and server sharing the 4 physical cores of a `1.60 GHz` mobile CPU, roundtrip latency at `c=1` is **`3.110–3.706 ms`** (`266.2–303.5 RPS`) across all implementations, and saturated loopback throughput at `c=16` ranges from **`1,165.4–1,391.9 RPS`**.
   - At these small payload sizes (`169 B` / `349 B`), loopback socket + `SocketsHttpHandler` + Kestrel framing dominates wall-clock time on a single 4-core laptop, yielding modest headline HTTP RPS gains for FastSoap (`1.19x` vs `corewcf-dcs` on Echo `c=16`, `1.04x` vs `corewcf-dcs` and `1.11x` vs `soapcore-xmlser` on Transfer `c=16`) despite `2.3x–31.2x` lower server memory allocation. Multi-host / dedicated-load-generator testing and Native AOT execution remain future work once a Linux/MSVC AOT runner is provisioned.
3. **Zero Errors:** All 20 HTTP load scenarios completed with **`0` errors**.
