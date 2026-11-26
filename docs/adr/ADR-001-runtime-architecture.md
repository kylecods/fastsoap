# ADR-001: Runtime Architecture Selection & Gate 0 Go/No-Go Decision

**Status:** Accepted (Conditional Go — Gate 0 Conditional Pass)  
**Date:** 2026-11-26  
**Deciders:** Gate 0 / R0-D Architecture Reviewer (reconciling R0-A, R0-B, R0-C, C0, R0-G)  
**Canonical Specification:** [FastSoap-Implementation-Plan-v2.md](../FastSoap-Implementation-Plan-v2.md) (§1.3, §2, §7.1, §14.3, §16)  
**Supporting Evidence:** [spike-results.md](../research/spike-results.md), [upstream-matrix.md](../research/upstream-matrix.md), [r0a-corewcf.md](../research/r0a-corewcf.md), [r0b-soapcore.md](../research/r0b-soapcore.md), [c0-xmlessentials-qualification.md](../research/c0-xmlessentials-qualification.md), [r0c-greenfield.md](../research/r0c-greenfield.md), [compatibility-corpus.md](../research/compatibility-corpus.md), [dependency-licenses.md](../research/dependency-licenses.md)

---

## 1. Context

`FastSoap-Implementation-Plan-v2.md` §1.3 requires an evidence-based Gate 0 decision before any production implementation begins, applying the following Decision Rule:

> *Favor an existing framework if it meets the agreed supported wire profile, AOT and performance criteria with materially lower maintenance/security burden. Choose the independent runtime only when the existing extension paths impose significant verified limitations or complexity and the minimal greenfield spike provides a viable alternative. If the results are inconclusive or the XML backend fails qualification, stop and resolve the blockers rather than committing to a rewrite.*

Three architectural candidates were evaluated in isolated spikes alongside dependency qualification (`C0`) and a live CoreWCF/WCF compatibility oracle (`R0-G`):

1. **R0-A — CoreWCF Extension (`CoreWCF.Http` / `CoreWCF.Primitives` `1.9.2`)**
2. **R0-B — SoapCore Adaptation (`SoapCore` `1.2.1.18`)**
3. **R0-C — Independent FastSoap Runtime on ASP.NET Core + `CloudNimble/XmlEssentials`**

Additionally, `dotnet/runtime #125299` (`System.Text.Xml`) was confirmed **closed as `not_planned`** on 2026-03-09, confirming the .NET BCL will not ship a parallel UTF-8 AOT XML serialization stack.

---

## 2. Decision

### 2.1 Architecture Selection: **GO on R0-C (Independent FastSoap Runtime)**

We **reject R0-A (CoreWCF extension)** and **reject R0-B (SoapCore adaptation)** as FastSoap's runtime base, and **approve R0-C (independent UTF-8-native SOAP runtime on ASP.NET Core)** under a **Conditional Pass** at Gate 0:

- **Why R0-A (CoreWCF `1.9.2`) is rejected:**
  - Even with custom `IDispatchMessageFormatter` and `MessageEncoder` extensions plus `CoreWCF.BuildTools` operation invoker generation active, `ILCompiler` (`10.0.15`) emits **343 ILC warnings** (`209 IL3050`, `74 IL2026`, `4 IL3054` generic recursion aborts that throw at runtime if reached) and `ILLink` emits **127 warnings**.
  - CoreWCF's public contracts unavoidably couple to `CoreWCF.Channels.Message`, `BodyWriter`, `System.Xml.XmlDictionaryReader`/`Writer`, `BufferManager`, and heavy transitive dependencies (`System.Web.Services.Description`, `System.Security.Cryptography.Xml`, `Microsoft.IdentityModel.*`, `System.DirectoryServices`).
  - Source-generated `IOperationInvoker`s still allocate `object[]` and box return values per request.
  - Upstream AOT/buffer PRs (`#1766`, `#1758`, `#801`) are unmerged; even draft `#1766` reports 339 trim/AOT warnings and covers operation body `DataContractSerializer` only.
  - **Retained role:** Keep released `CoreWCF 1.9.2` (with generated invokers enabled) strictly in test/oracle/benchmark projects as the primary wire compatibility oracle and JIT performance baseline.

- **Why R0-B (SoapCore `1.2.1.18`) is rejected:**
  - `SoapMessageEncoder` hardcodes `ParsedMessage.FromStreamAsync` -> `XDocument.LoadAsync` (full DOM buffering on every request) and mutates private `Message.State` via reflection.
  - `IXmlSerializationHandler` is ingress-only (receiving `XmlDictionaryReader` + runtime `Type`, forcing `ReadOuterXml()` -> `string` -> UTF-8 `byte[]` double-transcoding to reach `XmlEssentials`); egress serialization (`internal ServiceBodyWriter`) has no extension seam and hardcodes BCL `XmlSerializer`/`DataContractSerializer`.
  - Startup assembly reflection (`ServiceDescription`), `System.CodeDom` (`CSharpCodeProvider`), `MakeGenericMethod`, and `Activator.CreateInstance` permeate the pipeline.
  - Under `PublishTrimmed=true -p:TrimMode=full`, `SoapCore` emits **64 trim/AOT warnings** and **crashes immediately on startup** (`InvalidOperationException` caused by `IL2087`).
  - Verified security/protocol flaws in `1.2.1.18`: ignores `maxSizeOfHeaders` and `ReaderQuotas`, performs case-insensitive substring `SOAPAction` matching, unconditionally trusts `X-Forwarded-Host`, and leaks raw exception messages in `<faultstring>`.
  - **Retained role:** Keep `SoapCore 1.2.1.18` strictly in benchmark projects as a secondary JIT baseline.

- **Why R0-C (Independent FastSoap Runtime) is selected:**
  - Verified **0 `System.Xml*` assembly references** in both the runtime assembly and `CloudNimble.XmlEssentials.dll` (`PEReader`/`MetadataReader`).
  - Verified **0 trim/AOT warnings** in both Roslyn analyzers (`TreatWarningsAsErrors=true`, `IsAotCompatible=true`) and full `ilc.exe` (`10.0.15`) native object compilation (`56,973,102`-byte `.obj`) with `CloudNimble.Text.Xml.XmlSerializer.IsReflectionEnabledByDefault=false` trimmed.
  - Demonstrates a clean, safe, bounded UTF-8 pipeline (`PipeReader` -> `SoapRequestBuffer` -> synchronous `Utf8XmlReader` -> `XmlSerializer.Deserialize(ref reader, typeInfo)` -> static `FrozenDictionary` typed dispatch -> bounded staged `Utf8XmlWriter` reply) with full ownership control and zero legacy WCF channel/message baggage.

### 2.2 Pinned XML Backend Decision (C0 / ADR-011)

- **Package:** `XmlEssentials`
- **Exact Pinned Version:** **`1.0.0`** (Central Package Management exact pin, **no floating/range versions**)
- **Verified Commit SHA:** `44b8a0d90bd62c7f83e94f2b4c63f87defbd4134` (GitHub tag `v1.0.0`, published `2026-11-25T20:23:00Z`)
- **License:** `MIT` (0 NuGet dependencies; ships `lib/net10.0`, `lib/net11.0`, and `analyzers/dotnet/cs/CloudNimble.XmlEssentials.SourceGeneration.dll`)
- **Upgrade Freeze:** Do **not** upgrade to upstream tag `v1.0.1` (`f7bd3a3376db16b5d4151d0ac2fae26da001544e`, currently unpublished on NuGet, containing breaking `Order` and metadata API changes) without a separate re-qualification ADR.
- **Internal Seam Requirement (ADR-011):** All `CloudNimble.Text.Xml` calls must remain encapsulated inside `FastSoap.Core` behind narrow internal adapters. `FastSoap.Abstractions` must have **zero** dependency on `XmlEssentials`, `Microsoft.AspNetCore.App`, or `System.Xml*`.

### 2.3 Target Framework, SDK Pin & C# 15 Decision (Reconciling Plan §2 / ADR-002 & `AGENTS.md` §2)

To resolve the split observed during R0 (where R0-A/R0-C targeted `net10.0` while R0-B/R0-G targeted `net11.0`, and the host has `.NET 11.0.100` GA SDK + `.NET 10.0.100-rc.1` pre-release SDK):

| Layer | Target Framework | Language Version | SDK / Notes |
|---|---|---|---|
| **Repository SDK Pin (`global.json`)** | — | — | Pin **`11.0.100`** (`"rollForward": "latestFeature"`, `"allowPrerelease": false`). Required for C# 15 (`<LangVersion>15</LangVersion>`) and GA toolchain stability. |
| **Core & Hosting Runtime Libraries** (`FastSoap.Abstractions`, `FastSoap.Core`, `FastSoap.AspNetCore`, `FastSoap.Metadata`) | **`net10.0`** (LTS baseline) | **`15`** (`<LangVersion>15</LangVersion>`) | Verified compiling cleanly with `.NET 11.0.100` SDK against `Microsoft.NETCore.App 10.0.15`, `Microsoft.AspNetCore.App 10.0.15`, `XmlEssentials 1.0.0` (`lib/net10.0`), and `Microsoft.DotNet.ILCompiler 10.0.15`. Compiler enforces that no `net11.0`-only runtime/BCL APIs leak into `net10.0` binaries. |
| **Samples, Tests, Oracle & Benchmarks** (`samples/*`, `tests/*`, `benchmarks/*`) | **`net10.0`** | **`15`** (`<LangVersion>15</LangVersion>`) | All baselines, oracles, and runtime tests MUST execute on the exact same `net10.0` runtime (`10.0.15`) to eliminate cross-TFM measurement and behavior drift. |
| **Roslyn Generators & Analyzers** (`FastSoap.Generators`) | **`netstandard2.0`** | **`12.0`** (C# 12 subset per `AGENTS.md` §2) | Required for Roslyn analyzer/generator host compatibility. |

---

## 3. Gate 0 Status: **CONDITIONAL PASS** — Mandatory Constraints & Blockers for A0 and M1

Production foundation (`A0`) and vertical-slice work (`B1`, `C1`, `D1`, `G1`, `H1`) are **authorized to proceed**, strictly subject to the following non-negotiable constraints and exit blockers:

### 3.1 Mandatory Constraints for `A0` (Foundation Owner — Before Parallel M1 Fan-Out)

1. **Create Root Build & Governance Files:**
   - `global.json` pinning SDK `11.0.100`.
   - `Directory.Build.props` enforcing `net10.0`, `<LangVersion>15</LangVersion>`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>disable</ImplicitUsings>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<Deterministic>true</Deterministic>`, and `<EnableNETAnalyzers>true</EnableNETAnalyzers>`.
   - Runtime libraries (`FastSoap.Abstractions`, `FastSoap.Core`, `FastSoap.AspNetCore`, `FastSoap.Metadata`) MUST enable `<IsAotCompatible>true</IsAotCompatible>`, `<EnableTrimAnalyzer>true</EnableTrimAnalyzer>`, `<EnableAotAnalyzer>true</EnableAotAnalyzer>`, and `<TrimmerSingleWarn>false</TrimmerSingleWarn>`, with zero trim/AOT warning suppressions (`IL*`).
   - Executables/samples using `XmlEssentials` MUST include `<RuntimeHostConfigurationOption Include="CloudNimble.Text.Xml.XmlSerializer.IsReflectionEnabledByDefault" Value="false" Trim="true" />`.
   - `Directory.Packages.props` with Central Package Management (`ManagePackageVersionsCentrally=true`) pinning exact package versions (including `XmlEssentials` `1.0.0`, `CoreWCF.Http`/`Primitives` `1.9.2`, `SoapCore` `1.2.1.18`, `System.ServiceModel.Http`/`Primitives` `10.0.652802`, `xunit.v3` `3.2.2`).
   - Replace root placeholder `FastSoap.csproj` / `Class1.cs` with the canonical solution layout from Plan §2.2 (`src/`, `samples/`, `tests/`, `benchmarks/`).
   - Add an automated assembly-reference verification test proving **zero `System.Xml*` references** in `FastSoap.Abstractions`, `FastSoap.Core`, and `FastSoap.AspNetCore`.
2. **Complete `docs/research/dependency-licenses.md`:** Add verified versions, commit SHAs, and licenses for every test, oracle, benchmark, and Roslyn generator package before restoring them in `Directory.Packages.props`.
3. **Freeze Shared Contracts & ADRs (ADR-002..ADR-014):** Publish final public signatures in `FastSoap.Abstractions` and the exact SOAP 1.1/1.2 HTTP fault/status matrix (specifically resolving SOAP 1.2 `Sender` HTTP 400 per W3C spec vs CoreWCF's HTTP 500 `FaultException<T>` behavior).

### 3.2 Mandatory Constraints for `B1`, `C1`, and `D1` (Runtime Implementers)

All agents MUST obey these `XmlEssentials 1.0.0` guardrails and fix every defect identified in the R0-C spike audit:

1. **Mandatory `XmlEssentials 1.0.0` Guardrails (C1):**
   - Always configure `XmlReaderOptions` explicitly: `MaxDocumentByteCount = options.MaxRequestBytes` (default `0` is unbounded!), `MaxDepth = options.MaxXmlDepth` (64), `MaxAttributeCount = options.MaxAttributesPerElement` (64), `MaxNameByteCount = 1024`, `MaxTextByteCount`, `AllowMultipleRoots = false`.
   - Always construct shared `XmlSerializerContext` instances with `new XmlSerializerOptions { AllowDuplicateProperties = false, UnmappedMemberHandling = XmlUnmappedMemberHandling.Disallow, OmitXmlDeclaration = true }` (cannot be set via `[XmlSourceGenerationOptions]` in `1.0.0`).
   - Always declare explicit `Namespace = "..."` (or `Namespace = ""` for unqualified) on every `[XmlElement]` property in wire DTOs (because `Namespace = null` matches any namespace in `1.0.0`).
   - Always validate root expanded QName (`LocalNameEquals` AND `NamespaceUriEquals`, including empty namespace) explicitly using pre-encoded UTF-8 `ReadOnlySpan<byte>` tokens before invoking `XmlSerializer.Deserialize(ref reader, typeInfo)` (because `CheckRoot` skips namespace check when `_rootNamespaceUri.Length == 0`).
   - **Fail-Fast C1 Corpus Qualification Gate:** Before completing C1, run `XmlEssentials 1.0.0` tests covering: (a) `xsi:nil`/`i:nil`, (b) missing `required` properties in XML input, (c) collections with populated & nil items, (d) enums & UTC `DateTime` & `decimal`, (e) multi-namespace nested DTOs, (f) UTF-8 BOM & non-ASCII/emoji & `0x0F` control chars, (g) self-closing empty body elements, and (h) **processing instructions (`<?pi?>`) inside deserialized body elements**. If `XmlEssentials 1.0.0` fails any required behavior (e.g., silently skips PIs inside DTOs or ignores missing required fields), **stop and escalate** for an adapter workaround or upstream issue rather than shipping silent wire/security bugs.
2. **Mandatory Protocol Fixes from R0-C Audit (B1):**
   - Distinguish **non-Envelope XML root** (`NEG-10` -> HTTP `400` / Client error) from **Envelope with wrong namespace** (`NEG-13` -> SOAP `VersionMismatch`).
   - Enforce `MaxHeaderCount` (32) and `MaxHeaderBytes` (64 KiB) during header scanning, reject `encodingStyle` attributes on Envelope/Body, and respect SOAP 1.1 `actor` targeting (do not fault `mustUnderstand="1"` aimed at another actor).
   - Enforce strict SOAP 1.1 `SOAPAction` quoting/length bounds by default, with explicit opt-in compatibility switches only (`AllowMissingSoap11Action = false`).
   - Reconcile `XmlException` handling so malformed XML before envelope establishment returns HTTP `400`, while post-envelope deserialization/schema violations follow the agreed SOAP Fault matrix without leaking internal exception messages.
3. **Mandatory Buffer & Transport Fixes from R0-C Audit (C1 / D1):**
   - Inject `ArrayPool<byte>` into `SoapRequestBuffer` and `BoundedPooledBufferWriter` (defaulting to `ArrayPool<byte>.Shared`) and test with a deterministic tracking pool catching double returns, foreign returns, growth peak rentals, and exception-path cleanup.
   - Commit staged responses using `System.Buffers.BuffersExtensions.Write(response.BodyWriter, stagedBytes.Span)` (NOT `bodyWriter.GetSpan(stagedBytes.Length)`), preventing multi-megabyte contiguous pipe rentals.
   - Use plain `RequestDelegate` for SOAP POST endpoints (no minimal API reflection parameter binding), set `response.Headers.ContentType` and `response.ContentLength` via typed properties, and never attempt to write a fault if `response.HasStarted` or `RequestAborted.IsCancellationRequested`.

### 3.3 Mandatory Blockers Before M1 Gate Can Pass (`G1`, `H1`, `A1`)

1. **BLOCKER-1 (Real Linked Native AOT Execution):** M1 **cannot** pass on `ilc.exe` `.obj` analysis or JIT execution alone. Before M1 closes, CI or the local host MUST have a native C++ linker (MSVC C++ Build Tools + Windows SDK on Windows, and/or Linux `linux-x64` runner/container with `clang` + `zlib1g-dev`), publish a real `FastSoap.Sample.NativeAot` executable with `-c Release -p:PublishAot=true`, launch it as a separate process, and execute automated HTTP + WCF client tests against it with **0 trim/AOT warnings**.
2. **BLOCKER-2 (Upgrade R0-G Oracle & Unify Wire Contracts):** `G1` MUST upgrade `FastSoap.R0G.Oracle` from `CoreWCF 1.8.0` (**7 known CVEs**, including critical `NU1904`) to **`CoreWCF 1.9.2`** on **`net10.0`**, re-verify all 48 fixtures, add the missing security/protocol negative fixtures (PIs in envelope and body, XML 1.1, UTF-8 BOM, `encodingStyle`, depth/attribute/header quotas, duplicate DTO fields, non-targeted `actor`), and test a real `System.ServiceModel.Http` WCF client calling FastSoap.
3. **BLOCKER-3 (Execute R0-H / H1 Comparative Baselines):** No performance, latency, throughput, or allocation claims may be made until `H1` produces reproducible `net10.0` Release benchmarks (`BenchmarkDotNet` + real Kestrel HTTP load) comparing FastSoap (JIT + Native AOT) against released `CoreWCF 1.9.2` (with generated invokers verified active) and `SoapCore 1.2.1.18` on identical R0-G wire contracts.

---

## 4. Consequences

- **Positive:** Zero `System.Xml*` runtime dependencies; clean `ilc.exe` Native AOT trajectory (0 warnings); full control over UTF-8 parsing, buffer ownership, quotas, and security boundaries; eliminates CoreWCF's 343 ILC warnings and SoapCore's DOM/trimming/security defects.
- **Negative / Trade-offs:** FastSoap owns SOAP 1.1/1.2 envelope grammar, header/role semantics, fault formatting, and WSDL/XSD metadata emission directly; depends on a newly published `XmlEssentials 1.0.0` library whose upstream (`v1.0.1`) is actively evolving with breaking changes (mitigated by exact version pinning, C1 fail-fast corpus testing, and ADR-011 internal adapter isolation).
- **No Upstream Fork / No Premature PR:** FastSoap will not fork CoreWCF or SoapCore and will not autonomously open upstream discussions/PRs without maintainer approval.
