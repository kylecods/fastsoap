# Gate 0 Consolidated Comparative Spike Results (R0-D)

**Review Date:** 2026-11-26  
**Work Package:** R0-D (Gate 0 Architecture Review & Evidence Consolidation)  
**Canonical Specification:** [FastSoap-Implementation-Plan-v2.md](../FastSoap-Implementation-Plan-v2.md) (§1.3, §2, §7.1, §14.3, §16)  
**Reviewed Inputs:**
- [upstream-matrix.md](upstream-matrix.md) (R0-A)
- [r0a-corewcf.md](r0a-corewcf.md) (R0-A, [experiments/R0A-CoreWcfSpike/](../../experiments/R0A-CoreWcfSpike/))
- [r0b-soapcore.md](r0b-soapcore.md) (R0-B, [experiments/R0B-SoapCoreSpike/](../../experiments/R0B-SoapCoreSpike/))
- [c0-xmlessentials-qualification.md](c0-xmlessentials-qualification.md) (C0)
- [r0c-greenfield.md](r0c-greenfield.md) (R0-C, [experiments/R0C-GreenfieldSpike/](../../experiments/R0C-GreenfieldSpike/))
- [dependency-licenses.md](dependency-licenses.md) (C0/R0-C)
- [compatibility-corpus.md](compatibility-corpus.md) (R0-G, [experiments/R0G-Oracle/](../../experiments/R0G-Oracle/), [tests/fixtures/manifest.json](../../tests/fixtures/manifest.json))

---

## 1. Host Verification Environment

Recorded directly from `dotnet --info` on 2026-11-26 during the R0-D independent audit:

| Property | Observed Value |
|---|---|
| Operating System | Windows 10 (`10.0.22631`), `win-x64` |
| Active .NET SDK | `11.0.100` (commit `297acf930f`, MSBuild `18.12.2-1.26529.118+297acf930f`) |
| Other Installed SDKs | `10.0.100-rc.1.25451.107`, `8.0.428`, `6.0.321` (note: **no GA .NET 10 SDK** is installed; only RC1) |
| Installed .NET Runtimes | `Microsoft.NETCore.App` & `Microsoft.AspNetCore.App` `10.0.15` and `11.0.0` (plus older 3.1/5/6/8) |
| Native C++ Linker (`link.exe` / Windows SDK libs) | **Not installed** (Visual Studio Community 2026 `18.12.0` lacks `Microsoft.VisualStudio.Component.VC.Tools.x86.x64` and Windows SDK `Lib`/`Include`) |
| `global.json` at Repo Root | **Not present** (must be created in A0) |

> **Critical Transparency Statement on Native AOT Execution:**  
> **Zero linked Native AOT executables were produced or executed on this host** across R0-A, R0-B, and R0-C because MSVC `link.exe` and Windows SDK native libraries are absent. All AOT diagnostics below come from:
> 1. Roslyn trim/AOT analyzers (`IsAotCompatible=true`, `EnableAotAnalyzer=true`, `EnableTrimAnalyzer=true`),
> 2. `ILLink` (`PublishTrimmed=true`), and
> 3. `ILCompiler` (`ilc.exe` `10.0.15`) native object codegen (`-p:PublishAot=true -p:IlcUseEnvironmentalTools=true`), which runs full AOT reachability/codegen and emits the native `.obj` file before halting at `link.exe` (`MSB3073`).
>
> All live HTTP smoke tests executed against **JIT builds** (with `XmlSerializer.IsReflectionEnabledByDefault=false` in R0-C). True linked Native AOT binary execution remains an open hard gate for M1.

---

## 2. Consolidated Comparative Evidence Table

| Criterion | R0-A: CoreWCF Extension (`1.9.2`) | R0-B: SoapCore Adaptation (`1.2.1.18`) | R0-C: Greenfield Runtime (`XmlEssentials 1.0.0`) | R0-G: Compatibility Oracle (`CoreWCF 1.8.0` + WCF `10.0.652802`) |
|---|---|---|---|---|
| **Pinned Upstream Version & Commit** | `CoreWCF.Http` / `Primitives` `1.9.2` (`62a222a59084af52a94afccac37d842b930ea46b`, MIT) | `SoapCore` `1.2.1.18` (`4eb088b3eccc27a72b6040e8d43b7ab4317ae612`, MIT) | `XmlEssentials` `1.0.0` (`44b8a0d90bd62c7f83e94f2b4c63f87defbd4134`, MIT) | `CoreWCF` `1.8.0` (`15070b17...`, **7 NuGet CVEs**), `System.ServiceModel.Http` `10.0.652802` (MIT) |
| **Target Framework & C# Version Used** | `net10.0`, C# 15 (`<LangVersion>15</LangVersion>`) | `net11.0`, C# 15 (`<LangVersion>15</LangVersion>`) *(drift from Plan §2)* | `net10.0`, C# 15 (`<LangVersion>15</LangVersion>`) | `net11.0`, C# 15 (`<LangVersion>15</LangVersion>`) *(drift from Plan §2)* |
| **Spike Contracts Used** | `IEchoService` (`urn:echo`, `<message>`) + `IPaymentService` (`urn:payments`, 2-field `TransferRequest`) | Combined `IPaymentService` (`urn:payments`, `<message>`) + 2-field `Transfer` (alphabetical `TransferResult`) | `Echo` (`urn:echo`, `<Message>` capitalized) + `Transfer` (`urn:payments`, 2 fields) | `EchoString` (`tempuri.org`, `<echo>`) + 5-field `TransferRequest` + headers + collections + faults + SOAP 1.2 + WSDL |
| **Zero `System.Xml*` in Core Runtime** | **Failed.** Mandates `XmlDictionaryReader`/`Writer`, `Message`, `BodyWriter`; transitively pulls `System.Web.Services.Description`, `System.Security.Cryptography.Xml`, `Microsoft.IdentityModel.*`, `System.DirectoryServices` | **Failed.** Hardcodes `XDocument.LoadAsync`, `XDocumentXmlReader`, `XmlWriter`, `XmlDocument`, `System.ServiceModel.Http`, `System.CodeDom` | **Passed.** Verified via `PEReader`/`MetadataReader`: **0** `System.Xml*` assembly references in spike DLL and `CloudNimble.XmlEssentials.dll` | N/A (JIT-only oracle intentionally uses BCL `System.Xml`, CoreWCF, and WCF client) |
| **UTF-8 Native / No DOM Round-Trip** | **Partial.** `XmlUTF8TextReader` avoids full UTF-16 message copy, but `XmlDictionaryReader` / `DataContractSerializer` materializes UTF-16 strings per element and buffers via `Message` + `BufferManager` | **Failed.** `ParsedMessage.FromStreamAsync` parses every request into `XDocument` DOM via `StreamReader`; `IXmlSerializationHandler` requires `ReadOuterXml()` -> `string` -> UTF-8 `byte[]` double-transcoding; egress uses `MemoryStream` + `XmlWriter` | **Passed (bounded copy).** Incremental `PipeReader` -> bounded pooled `byte[]` -> synchronous `Utf8XmlReader` -> `XmlSerializer.Deserialize(ref reader, typeInfo)` -> staged `Utf8XmlWriter` | N/A (oracle) |
| **Dispatch & Reflection-Free Boundary** | **Partial.** `CoreWCF.BuildTools` generates `OperationInvoker0`/`1` by default, but `IOperationInvoker` still allocates `object[]` (`AllocateInputs`) + `ValueTask<(object, object[])>`; `TypeLoader`, `PrimitiveOperationFormatter`, `FaultFormatter`, `SctClaimSerializer` still use reflection | **Failed.** `ServiceDescription` scans assemblies via reflection at startup; `IOperationInvoker` still receives `MethodInfo` + boxed `object[]`; `ServiceBodyWriter` is `internal` with no egress hook; `CSharpCodeProvider` (`CodeDom`) + `MakeGenericMethod` + `Activator.CreateInstance` | **Passed.** Static `FrozenDictionary<string, ISoapOperation>` + generic `SoapOperation<TRequest, TResponse>` + `XmlSerializer.IsReflectionEnabledByDefault=false` (trimmed) | N/A (oracle) |
| **ILLink Trim Analysis (`PublishTrimmed`)** | **127 ILLink warnings** (`TrimMode=partial`); binary ran because CoreWCF assemblies were not trimmed | **64 trim/AOT warnings** (`TrimMode=full`: 53 `SoapCore`, 10 `ServiceModel`, 1 `CodeDom`); **crashed immediately on startup** (`InvalidOperationException` via `IL2087`) | **0 warnings** (`PublishTrimmed=true`, `PublishAot=false`), 0 `System.Xml*.dll` files; Kestrel failed startup with `TypeLoadException` (`X509Certificate2Collection.GetEnumerator` — known ASP.NET Core 10 unsupported mode; `PublishAot=true` required) | N/A |
| **ILCompiler (`ilc.exe`) AOT Analysis** | **343 ILC warnings** (`209 IL3050`, `74 IL2026`, `4 IL3054` generic recursion aborts, etc.) before halting at missing `link.exe` | **Not run with `IlcUseEnvironmentalTools=true`** (halted pre-ILC at `vswhere.exe`); true `IL3050` count is therefore under-reported relative to ILLink's 64 warnings | **0 trim/AOT warnings**; `ilc.exe` (`10.0.15`) completed native codegen (`56,973,102`-byte `.obj`) before halting at missing `link.exe` | N/A |
| **Linked Native AOT Executable Executed** | **No** (host missing MSVC `link.exe`) | **No** (host missing MSVC `link.exe`, and broken under trimming) | **No** (host missing MSVC `link.exe`) | N/A |
| **Live JIT HTTP Verification (Independently Re-run in R0-D)** | **Pass** (`dotnet run -c Release`, exit `0`; 6 positive endpoints + 1 action mismatch) | **Pass** (`dotnet run -c Release -- --smoke-test`, exit `0`; 4 positive + 2 faults + WSDL) | **Build Pass** (`dotnet build -c Release`, 0 warnings); HTTP matrix previously executed manually (no checked-in smoke harness!) | **Pass** (`dotnet test -c Release`, **4/4 passed**, 48/48 SHA-256 fixtures verified; 7 NU190x warnings on `CoreWCF.Primitives 1.8.0`) |
| **Security & Protocol Hygiene** | Mature WCF stack, but massive attack/dependency surface and unmerged buffer leak fixes (`#1758`) + chunked transfer `FIXME`s | **Poor.** Ignores `maxSizeOfHeaders`/`ReaderQuotas`; case-insensitive substring `SOAPAction` match; unconditionally trusts `X-Forwarded-Host`; leaks raw exception messages in `<faultstring>` | Strict bounded quotas, XML 1.0 + DTD + PI (envelope-level) + UTF-8 validation; several spike-level bugs identified in R0-D audit (see §4) | Captures 15 adversarial vectors (`NEG-01`..`NEG-13`) verbatim against CoreWCF `1.8.0` |
| **Comparative Performance (R0-H)** | **Not measured** (R0-H not executed) | **Not measured** (R0-H not executed) | **Not measured** (only single-run JIT startup `1451.03 ms` recorded; no throughput/allocs) | N/A |
| **Gate 0 Verdict** | **Reject as runtime base** (keep as JIT baseline/oracle) | **Reject as runtime base** (keep as JIT benchmark baseline) | **Select (Conditional Pass)** — subject to ADR-001 constraints | **Accept corpus (with 1.9.2 upgrade & gap fixes in G1)** |

---

## 3. Claims vs. Evidence vs. Unresolved Questions

| # | Claim / Hypothesis | Evidence Observed (Verified in R0-D) | Status | Unresolved Questions / Required Action |
|---|---|---|---|---|
| 1 | Extending released CoreWCF (`1.9.2`) via custom formatter/encoder cannot meet FastSoap's zero-`System.Xml`, warning-free Native AOT goals | Verified live: custom `IDispatchMessageFormatter` + `MessageEncoder` still force `Message`/`XmlDictionaryReader`/`XmlDictionaryWriter`/`BufferManager`, generate **343 ILC warnings** (incl. 4 `IL3054` runtime-throwing aborts), and retain `object[]` invokers. Upstream PR `#1766` (unmerged draft) still reports 339 warnings and omits primitives/faults/headers. | **Verified** | None blocking Gate 0. Retain `CoreWCF 1.9.2` (with generated invokers) as primary JIT benchmark & oracle baseline. |
| 2 | Adapting `SoapCore` (`1.2.1.18`) is not viable without a full rewrite | Verified directly against commit `4eb088b3...` and live spike: `XDocument` DOM buffering in `ParsedMessage`, no egress serializer seam, startup reflection + `CodeDom`, 64 trim warnings + immediate crash under `TrimMode=full`, security defects (`ReaderQuotas` ignored, substring action match, `X-Forwarded-Host` trust). | **Verified** | Full `ilc.exe` (`IL3050`) warning count was not run for R0-B (unnecessary given full-trim crash and structural blockers). Retain `SoapCore 1.2.1.18` as JIT benchmark baseline only. |
| 3 | BCL will provide a modern `System.Text.Xml` AOT serializer | Verified via GitHub API: `dotnet/runtime #125299` was **closed as `not_planned`** on 2026-03-09 by `@eiriktsarpalis`; `#106580` (`XmlSerializer` AOT) and `#32555` (async XML) remain open. | **Verified** | Monitor `dotnet/runtime #106580`, but proceed with `XmlEssentials` behind an internal adapter (ADR-011). |
| 4 | `XmlEssentials 1.0.0` is AOT-clean, MIT-licensed, zero-dependency, and `System.Xml`-free | Verified via `.nupkg` manifest, `MetadataReader` (`0` `System.Xml*` refs), Roslyn analyzers (`0` warnings), and `ilc.exe` (`0` warnings, `56.97 MB` `.obj`) with `IsReflectionEnabledByDefault=false`. | **Verified (with guardrails)** | Upstream `v1.0.1` tag (`f7bd3a33...`) already introduced breaking API/`Order` changes (not yet on NuGet). **Must pin exact `1.0.0`** and isolate behind internal adapter (ADR-011). |
| 5 | `XmlEssentials 1.0.0` satisfies the full supported FastSoap wire profile (R0-G 32 supported fixtures) | **Partially verified only.** Tested on simple 2-field `Echo` and `Transfer` (default and prefixed `urn:payments`). **Not yet tested** on `xsi:nil`/`i:nil`, collections (`List<T>` with nil items), enums, UTC `DateTime`, multi-byte UTF-8 (`🚀`), UTF-8 BOM, `0x0F` control char, `required` property missing-element enforcement, multi-namespace nested DTOs, self-closing `<Body/>` children, or PIs inside deserialized DTO payloads. | **Partially Verified (Gap)** | **C1/G1 Fail-Fast Gate:** Execute `XmlEssentials 1.0.0` against all 32 supported + 15 negative R0-G fixtures immediately in M1 before building higher layers. |
| 6 | R0-C compiles and runs as a real linked Native AOT HTTP server with 0 warnings | **Partially verified.** `ilc.exe` compiled native `.obj` with **0 warnings**, proving AOT codegen cleanliness. However, **no linked native `.exe` was produced or run** due to missing MSVC `link.exe` on Windows host. Live HTTP tests ran on JIT (`IsReflectionEnabledByDefault=false`). | **Blocked by Host Toolchain** | **M1 Hard Gate:** Provision MSVC C++ Build Tools + Windows SDK (or Linux `linux-x64` runner/container with `clang`), publish actual native binary, and run automated HTTP + WCF client smoke tests against it. |
| 7 | FastSoap delivers >= 1.3x throughput / -25% CPU / -30% allocations vs CoreWCF JIT (generated invokers) and SoapCore | **Completely unverified.** Work package **R0-H** was not executed; `benchmarks/` does not exist. Only single-run JIT startup (`1451.03 ms`) was logged for R0-C. | **Unverified (Hypothesis)** | **No performance claims permitted.** Execute R0-H / H1 comparative benchmarks on unified `net10.0` runtime and identical R0-G wire contracts before M1/M2 performance reporting. |
| 8 | R0-C HTTP test matrix is fully reproducible from checked-in code | **Gap found in R0-D audit.** `experiments/R0C-GreenfieldSpike/Program.cs` starts Kestrel and waits; the 10-case HTTP test client that produced `r0c-greenfield.md` §3 was an ad-hoc external script not committed or documented with exact reproduction commands. | **Gap** | All M1+ suites must have checked-in automated xUnit / Kestrel integration harnesses (no ad-hoc manual scripts). |
| 9 | R0-G compatibility corpus accurately captures CoreWCF + WCF client wire behavior with clean provenance | **Verified with version drift.** 48/48 fixtures verified live (`4/4` xUnit tests pass in `215 ms`), SHA-256 checked, 0 denominator manipulation. However, R0-G references **`CoreWCF 1.8.0`** (triggering **7 NuGet security advisories**, incl. critical `NU1904` `GHSA-xjr9-gg9q-jx3v`) instead of `1.9.2`, and targets `net11.0` instead of `net10.0`. | **Verified (Needs Upgrade)** | **G1 Action:** Upgrade oracle to `CoreWCF 1.9.2` on `net10.0`, add missing negative cases (PIs in envelope/body, XML 1.1, UTF-8 BOM, `encodingStyle`, depth/attribute/header-count quotas, duplicate DTO fields, non-targeted `actor` `mustUnderstand`), and test a real WCF client calling FastSoap. |
| 10 | Target framework `.NET 10 LTS` reconciles cleanly with C# 15 (`<LangVersion>15</LangVersion>`) | Verified in R0-A and R0-C: `.NET 11.0.100` SDK compiles `net10.0` projects with `<LangVersion>15</LangVersion>` cleanly (`0` warnings) and executes on `Microsoft.NETCore.App 10.0.15`. Host's `.NET 10` SDK is RC1 (`10.0.100-rc.1`), which cannot compile C# 15. | **Verified** | Pin SDK `11.0.100` in `global.json`, unify all runtime/test/oracle/benchmark projects on `net10.0` + `<LangVersion>15</LangVersion>`, and keep `FastSoap.Generators` on `netstandard2.0` (C# 12 subset). |

---

## 4. Specific Code & Protocol Defects Found During R0-D Audit of `R0C-GreenfieldSpike`

Because `experiments/R0C-GreenfieldSpike/` is a throwaway spike and NOT production code, these defects do not block Gate 0 — **however, B1, C1, and D1 agents MUST NOT copy these patterns into `src/`:**

1. **Non-Envelope root mapped to `VersionMismatch` (`SoapEnvelopeReader.cs:312-315`):** Any root element other than `{soap11}Envelope` (such as `<Transfer xmlns="urn:payments">`, `NEG-10`) currently emits SOAP 1.1 `VersionMismatch` HTTP 500 instead of HTTP 400 / Client fault. `VersionMismatch` applies only when root local name is `Envelope` with an unsupported namespace.
2. **2 MiB contiguous `PipeWriter.GetSpan` request (`SoapResponseWriter.cs:136`):** `bodyWriter.GetSpan(stagedBytes.Length)` forces Kestrel's `PipeWriter` to allocate a single contiguous buffer up to `MaxResponseBytes` (2 MiB). Production code must use `bodyWriter.Write(stagedBytes.Span)` (`System.Buffers.BuffersExtensions.Write`) to copy into Kestrel's pooled segments.
3. **Hardcoded `ArrayPool<byte>.Shared` (`SoapRequestBuffer.cs`, `BoundedPooledBufferWriter.cs`):** Prevents deterministic tracking-pool verification (double return, foreign return, peak growth accounting) mandated by Plan §5.1 and ADR-013. Production buffers must accept an injectable `ArrayPool<byte>`.
4. **UTF-16 string QName comparison on hot path (`SoapEnvelopeReader.cs:404`):** Calls `reader.LocalNameEquals(string)` / `reader.NamespaceUriEquals(string)` instead of pre-encoded `ReadOnlySpan<byte>` UTF-8 tokens cached on the operation descriptor.
5. **Unbounded SOAP Header skipping & missing `actor` targeting (`SoapEnvelopeReader.cs:448-491`):** Does not enforce `MaxHeaderCount` (32) or `MaxHeaderBytes` (64 KiB), and does not distinguish SOAP 1.1 `actor` / SOAP 1.2 `role` targeting before raising `MustUnderstand`.
6. **Missing `encodingStyle` rejection & unverified PI handling inside body payload (`SoapEnvelopeReader.cs:188, 423-446`):** Does not reject `soap:encodingStyle` on `Envelope`/`Body`, and has not verified whether `XmlSerializer.Deserialize(ref reader, typeInfo)` rejects or silently skips `ProcessingInstruction` tokens appearing inside the deserialized body subtree.
7. **Lenient `SOAPAction` parsing (`Program.cs:227-254`):** Accepts unquoted `SOAPAction` without explicit compatibility opt-in and lacks a maximum `SOAPAction` length bound before string allocation.
8. **Duplicate-element fault mapping (`SoapEnvelopeReader.cs:382-385`):** Maps all `XmlException`s (including `AllowDuplicateProperties = false` violations after a valid SOAP Envelope was established) to HTTP `400 text/plain` rather than distinguishing pre-envelope malformation from SOAP 1.1 `s:Client` processing faults per the fault/status matrix (§6.4).

---

## 5. Summary of Gaps in Supporting Deliverables

- **[dependency-licenses.md](dependency-licenses.md):** Currently documents only C0/R0-C packages (`XmlEssentials 1.0.0`, `Microsoft.AspNetCore.App`, `Microsoft.DotNet.ILCompiler`). Must be expanded in A0 to record exact versions, commits, and licenses for test/oracle/baseline packages (`CoreWCF 1.9.2`, `SoapCore 1.2.1.18`, `System.ServiceModel.Http 10.0.652802`, `xunit.v3 3.2.2`, `BenchmarkDotNet`) and Roslyn analyzer packages (`Microsoft.CodeAnalysis.CSharp`).
- **Contract Harmonization Across Spikes/Oracle:** R0-A, R0-B, R0-C, and R0-G each used slightly different `Echo`/`Transfer` schemas (`<message>` vs `<Message>` vs `<echo>`, `urn:echo` vs `urn:payments` vs `http://tempuri.org/`, 2-field vs 5-field `TransferRequest`). A0/G1 must lock `tests/fixtures/` (R0-G) as the single canonical contract set for all M1/M2 tests and H1 benchmarks.
