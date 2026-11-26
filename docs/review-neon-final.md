# FastSoap Final Code, Architecture, Security & Performance Review

**Reviewer Role:** Final Code, Architecture, Security & Performance Reviewer (Gemini 4 Neon High)  
**Review Date:** 2026-11-26  
**Environment:** Windows (`win-x64`, OS `10.0.22631`), `.NET SDK 11.0.100` (commit `297acf930f`), Runtime `.NET 10.0.15` (`Microsoft.NETCore.App 10.0.15`, `Microsoft.AspNetCore.App 10.0.15`), `XmlEssentials 1.0.0`  
**Canonical References Audited Against:** `AGENTS.md`, `docs/FastSoap-Implementation-Plan-v2.md`, `docs/adr/ADR-001-runtime-architecture.md`, `docs/adr/ADR-002-to-014-foundation-decisions.md`, `docs/references/blazing-fast-dotnet.md`

---

## 1. Overall Verdict

### **CHANGES REQUESTED**

The FastSoap codebase demonstrates strong architectural discipline: the entire 14-project solution builds cleanly in `Release` with **0 warnings and 0 errors** (`TreatWarningsAsErrors=true`, `IsAotCompatible=true`, `EnableTrimAnalyzer=true`, `EnableAotAnalyzer=true`, `TrimmerSingleWarn=false`), all **240** tests pass (`0` failed, `0` skipped), runtime libraries contain **zero `System.Xml*` references** and **zero forbidden trim/AOT suppressions or reflection fallbacks**, and live `System.ServiceModel.Http` WCF client interop passes for both SOAP 1.1 and SOAP 1.2.

However, the audit identified **1 Critical** and **4 Major** concrete issues that must be addressed before M1/v0.1 sign-off:
1. **(Critical)** Source-generated endpoints (`FastSoap.Generators`) bind to `<Context>.Default`, bypassing `XmlEssentials 1.0.0` strict `XmlSerializerOptions` (`AllowDuplicateProperties = false`, `UnmappedMemberHandling = Disallow`), allowing duplicate XML elements (`NEG-19`) and unmapped members on every generated endpoint.
2. **(Major)** `SoapEnvelopeReader.ParseAndPrepare` calls `XmlEssentialsAdapter.EnsureNoProcessingInstructions` outside its `try/catch (XmlException)` boundary (turning malformed XML containing `<?` into unhandled `500` server faults), and `EnsureNoProcessingInstructions`'s `<?xml` fast-path does not require offset `0`/BOM.
3. **(Major)** `SoapHeaderProcessor.ProcessAsync` invokes registered `ISoapHeaderHandler` implementations before validating that all targeted mandatory headers are understood.
4. **(Major)** `GoldenCorpusTests` counts unserved WSDL files (`3/4`) and un-deserialized echo serializer fragments (`2/12`) as passed, and `docs/research/compatibility-corpus.md` still reports all 63 entries as `Untested`.
5. **(Major)** Redundant double-parsing of `Content-Type` / `SOAPAction` across `SoapHttpEndpointHandler` and `SoapMessageProcessor`, plus avoidable per-request `string` allocations on the UTF-8 hot path.

Additionally, **real linked Native AOT execution (`ADR-001` BLOCKER-1)** remains blocked on this Windows host due to missing MSVC C++ `link.exe`, and **comparative benchmark execution (`ADR-001` BLOCKER-3 / `ADR-014`)** is outside `FastSoap.slnx` (`benchmarks/FastSoap.Benchmarks/` exists on disk, not yet wired into `FastSoap.slnx` or recorded in a `docs/performance.md` report).

---

## 2. Verified Build & Test Matrix

### 2.1 Release Build
- **Command:** `dotnet build FastSoap.slnx -c Release`
- **Result:** **Succeeded** — `0 Warning(s)`, `0 Error(s)` (`Time Elapsed 00:00:09.71`)
- **Projects Built (14):** `FastSoap.Abstractions`, `FastSoap.Core`, `FastSoap.Metadata`, `FastSoap.AspNetCore`, `FastSoap.Generators` (`netstandard2.0`), `FastSoap.Sample.NativeAot`, `FastSoap.Sample.Payments`, and all 7 test projects.

### 2.2 Full Test Suite
- **Command:** `dotnet test FastSoap.slnx -c Release --no-build -m:1 --verbosity normal`
- **Result:** **Succeeded** — **240 Passed, 0 Failed, 0 Skipped** (`Time Elapsed 00:00:18.92`)

| Test Project | Total | Passed | Failed | Skipped | Observed Time |
|---|---:|---:|---:|---:|---:|
| `tests/FastSoap.CompatibilityTests` | 3 | 3 | 0 | 0 | 1.9540 s |
| `tests/FastSoap.GeneratorTests` | 35 | 35 | 0 | 0 | 4.4556 s |
| `tests/FastSoap.IntegrationTests` | 6 | 6 | 0 | 0 | 2.6610 s |
| `tests/FastSoap.MetadataTests` | 58 | 58 | 0 | 0 | 2.0654 s |
| `tests/FastSoap.ProtocolTests` | 69 | 69 | 0 | 0 | 2.0702 s |
| `tests/FastSoap.SerializationTests` | 20 | 20 | 0 | 0 | 1.6065 s |
| `tests/FastSoap.UnitTests` | 49 | 49 | 0 | 0 | 1.5909 s |
| **Solution Total** | **240** | **240** | **0** | **0** | **18.92 s** |

### 2.3 Native AOT Publish Probe
- **Command:** `dotnet publish samples/FastSoap.Sample.NativeAot/FastSoap.Sample.NativeAot.csproj -c Release -r win-x64 --no-restore`
- **Result:** Managed compilation of all projects succeeded with `0` warnings; failed at native linking prerequisite check:
  `Microsoft.NETCore.Native.Windows.targets(142,5): error : Platform linker not found.` (No Visual Studio C++ workload / `link.exe` installed on host).

---

## 3. Security, AOT, and CoreWCF Compatibility Checklist

| Audit Gate | Status | Evidence |
|---|---|---|
| **Zero `System.Xml*` in runtime assemblies** | **PASS** | `tests/FastSoap.UnitTests/AssemblyReferenceTests.cs` and `tests/FastSoap.MetadataTests/AssemblyReferenceTests.cs` inspect compiled PE metadata via `PEReader`/`MetadataReader`, proving zero `System.Xml*` references in `FastSoap.Abstractions`, `FastSoap.Core`, `FastSoap.AspNetCore`, `FastSoap.Metadata`, and `CloudNimble.XmlEssentials`. |
| **Zero trim/AOT suppressions or reflection fallback in `src/`** | **PASS** | Repository-wide scan of `src/**/*.{cs,csproj}` confirmed zero `UnconditionalSuppressMessage`, `#pragma warning disable IL*`, `<NoWarn>` for `IL*`, `MethodInfo.Invoke`, `DynamicInvoke`, `Activator.CreateInstance`, `MakeGenericMethod`, `Type.GetType`, or `unsafe` blocks. |
| **Runtime AOT analyzer settings & reflection trim switch** | **PASS** | All 4 runtime libraries enable `IsAotCompatible`, `EnableTrimAnalyzer`, `EnableAotAnalyzer`, `TrimmerSingleWarn=false`. Both samples set `CloudNimble.Text.Xml.XmlSerializer.IsReflectionEnabledByDefault=false` (`Trim="true"`). |
| **DTD / XXE / Billion Laughs / XML 1.1 / C0 Control Bytes** | **PASS** | Rejected with HTTP `400` and `0` service invocations across `ProtocolTests`, `IntegrationTests`, and `CompatibilityTests` (`NEG-01`, `NEG-02`, `NEG-03A`, `NEG-03B`, `NEG-15`). |
| **Processing Instructions (Envelope & Body)** | **PARTIAL (Finding M1)** | Tested and rejected for standard `<?pi?>` (`NEG-14A`, `NEG-14B`), but `EnsureNoProcessingInstructions` has a `try/catch` escape bug and a `<?xml` non-zero-offset fast-path flaw. |
| **Duplicate Properties & Unmapped Members** | **FAIL on Generated Path (Finding C1)** | Enforced on manual endpoints using strict `XmlSerializerOptions` (`NEG-19`), but **bypassed** on source-generated endpoints that emit `<Context>.Default`. |
| **Quotas (`MaxRequestBytes`, `MaxResponseBytes`, `MaxXmlDepth`, `MaxAttributesPerElement`, `MaxHeaderCount`, `MaxHeaderBytes`, `MaxActionLength`)** | **PASS** | Enforced fail-first with deterministic `TrackingArrayPool` ownership/clearing tests (`SoapBufferOwnershipTests.cs`) and Kestrel known-length + chunked `413` tests (`KestrelIntegrationTests.cs:269-285`). |
| **Fault Redaction & No-Leakage** | **PASS** | Unhandled exceptions map to sanitized `s:Server`/`s:Receiver` faults with zero exception message/stack leakage (`SoapFaultWriter.cs:47-82`, `WcfClientCompatibilityTests.cs:40-44,127-135,187-195`). |
| **Metadata Security (`X-Forwarded-Host`, Path Traversal, ETag)** | **PASS** | `SoapMetadataAddressPolicy` strictly ignores `Host`/`X-Forwarded-Host` (`KestrelIntegrationTests.cs:156-171`), `SoapMetadataPathNormalizer` rejects `..`/URL/traversal inputs, and strong SHA-256 ETags return `304`. |
| **Buffer Ownership & `BuffersExtensions.Write`** | **PASS** | `SoapRequestBuffer` and `BoundedPooledBufferWriter` accept injectable `ArrayPool<byte>`, clear used bytes when `ClearPooledBuffers=true`, track logical length vs capacity, and commit via `BuffersExtensions.Write` (`SoapBufferOwnershipTests.cs:25-46`). |
| **Roslyn Incremental Generator Model Hygiene** | **PASS** | `FastSoapContractGenerator` uses strictly value-equatable models (`ServiceContractModels.cs`, `EquatableArray.cs`, `EquatableLocation.cs`) with zero cached `ISymbol`/`Compilation`/`SyntaxTree`/`Diagnostic` references; verified by `IncrementalCachingTests.cs` (no-op, unrelated edit, trivia shift, contract edit) and `TwoGeneratorIntegrationTests.cs`. |
| **ASP.NET Core Hot Path (`RequestDelegate`, Typed Headers, `[LoggerMessage]`, Telemetry)** | **PASS WITH NOTES (Finding M4)** | Uses plain `RequestDelegate`, typed `Headers.ContentType`/`ContentLength`/`ETag`, `[LoggerMessage]` guarded with `IsEnabled`, and low-cardinality `ActivitySource`/`Meter` tags (`KestrelIntegrationTests.cs:430-455`). Double-parsing and string allocations remain (Finding M4). |
| **Real WCF `System.ServiceModel.Http` Interop** | **PASS** | `WcfClientCompatibilityTests.cs` verifies live `ChannelFactory<T>` calls over Kestrel loopback for SOAP 1.1 (`BasicHttpBinding`) and SOAP 1.2 (`CustomBinding` `MessageVersion.Soap12`) across echo, UTF-8 non-ASCII/emoji, `nil`, nested DTOs, collections, and faults. |
| **Test Rules (No Mocking, No AAA Comments, Exact Assertions)** | **PASS WITH NOTES (Finding N1)** | Zero mocking frameworks and zero `// Arrange`/`// Act`/`// Assert` comments across `tests/`. One `Assert.Contains` on XML output found in `SoapMessageProcessorAndFixtureTests.cs:76`. |

---

## 4. Concrete Findings

### Critical

#### **C1. Source-Generated Endpoints Bypass Strict `XmlSerializerOptions` (`AllowDuplicateProperties = false`, `UnmappedMemberHandling = Disallow`)**
- **Location:** `src/FastSoap.Generators/Emission/SoapContractEmitter.cs:135-136`
- **Affected Consumers:** `samples/FastSoap.Sample.Payments/Program.cs:89`, `samples/FastSoap.Sample.NativeAot/Program.cs:85`, `tests/FastSoap.IntegrationTests/KestrelIntegrationTests.cs:632,669`
- **Issue:** `SoapContractEmitter.EmitRegistrationExtensions` emits:
  ```csharp
  requestType: <ContextFqn>.Default.<RequestContextPropertyName>,
  responseType: <ContextFqn>.Default.<ResponseContextPropertyName>,
  ```
  As verified during C0 qualification (`docs/research/c0-xmlessentials-qualification.md:57`) and mandated by `ADR-001 §3.2 #1` and `ADR-011`, pinned `XmlEssentials 1.0.0` defaults `XmlSerializerOptions.AllowDuplicateProperties` to `true` and `UnmappedMemberHandling` to `Skip`, and `[XmlSourceGenerationOptions]` cannot override them in `1.0.0`. Consequently, `<Context>.Default` (confirmed emitting `<?xml ...?>` in `tests/FastSoap.GeneratorTests/TwoGeneratorIntegrationTests.cs:324`) does **not** enforce duplicate-element rejection (`NEG-19`) or unmapped-member rejection. Every generated `Map<ServiceName>(...)` endpoint silently accepts duplicate financial fields (e.g., two `<Amount>` elements) and unknown XML elements.
- **Recommendation:** Emit a static cached context instance per distinct context type in the generated extension class initialized with strict `XmlSerializerOptions`:
  ```csharp
  private static readonly <ContextFqn> s_context = new <ContextFqn>(new global::CloudNimble.Text.Xml.XmlSerializerOptions
  {
      AllowDuplicateProperties = false,
      OmitXmlDeclaration = true,
      UnmappedMemberHandling = global::CloudNimble.Text.Xml.XmlUnmappedMemberHandling.Disallow
  });
  ```
  Add an integration test on `/generated/PaymentService.svc` verifying duplicate-property (`NEG-19`) and unmapped-member rejection parity with `/manual/PaymentService.svc`.

---

### Major

#### **M1. `EnsureNoProcessingInstructions` Runs Outside `try/catch (XmlException)` and Has a Non-Start `<?xml` Fast-Path Bypass**
- **Locations:**
  - `src/FastSoap.Core/Protocol/SoapEnvelopeReader.cs:80` (outside `try` starting at line `89`)
  - `src/FastSoap.Core/Dispatching/SoapMessageProcessor.cs:280-294` (catches only `SoapProtocolException`)
  - `src/FastSoap.Core/Serialization/XmlEssentialsAdapter.cs:90-113`
- **Issues:**
  1. **Uncaught `XmlException` Escape:** `SoapEnvelopeReader.ParseAndPrepare` calls `XmlEssentialsAdapter.EnsureNoProcessingInstructions(utf8Xml, readerOptions)` at line `80` before entering `try { ... } catch (XmlException)` at line `89`. When an input contains `<?` (triggering the full `Utf8XmlReader` scan at `XmlEssentialsAdapter.cs:109-112`) combined with malformed XML (e.g., DTD, broken tag, or attribute/depth overflow), `XmlException` escapes both `SoapEnvelopeReader` and `SoapMessageProcessor`, landing in `SoapHttpEndpointHandler.HandlePostAsync`'s `catch (Exception)` (`SoapHttpEndpointHandler.cs:406`). This returns HTTP `500` `s:Server`/`s:Receiver` + an `Error` log instead of HTTP `400 Bad Request`.
  2. **Fast-Path Offset Bug:** `XmlEssentialsAdapter.cs:99-106` skips the scan whenever `utf8Xml` contains a single `"<?"` followed by `"xml"` + whitespace/`?` **anywhere in the buffer** — it never checks that `firstPiMarker == 0` (or `firstPiMarker == 3` immediately following a UTF-8 BOM `EF BB BF`). A document without an XML declaration containing `<?xml foo?>` inside the body bypasses the pre-scan.
  3. **Mismatched Scratch Size:** `XmlEssentialsAdapter.cs:108` allocates `stackalloc byte[512]`, smaller than `MaxNameByteCount = 1024` (`XmlEssentialsAdapter.cs:77`) and `SoapEnvelopeReader.cs:82`'s `1024`-byte scratch buffer.
- **Recommendation:** Move `EnsureNoProcessingInstructions` inside the `try { ... } catch (XmlException)` block in `SoapEnvelopeReader.cs`, restrict the `startsWithXmlDecl` fast-path to `firstPiMarker == 0 || (firstPiMarker == 3 && utf8Xml.StartsWith([0xEF, 0xBB, 0xBF]))`, and match the `1024`-byte scratch buffer size. Add negative tests for malformed XML containing `<?` and body-embedded `<?xml ...?>`.

#### **M2. `SoapHeaderProcessor.ProcessAsync` Executes Header Handlers Before Checking Mandatory Header Understanding**
- **Location:** `src/FastSoap.Core/Dispatching/SoapHeaderProcessor.cs:174-200`
- **Issue:** In a single loop over `context.Headers`, `ProcessAsync` immediately executes `await handler.HandleAsync(header, context, cancellationToken)` for recognized headers, while only setting `hasUnprocessedMandatory = true` for unrecognized targeted mandatory headers and throwing `SoapFaultKind.MustUnderstand` *after* the loop finishes. If header `#1` is handled by a registered `ISoapHeaderHandler` (e.g., auditing/state mutation) and header `#2` is an unknown `mustUnderstand="1"` header targeted at this node, header `#1` executes even though the message must be rejected with `MustUnderstand` prior to any processing (SOAP 1.1 §4.2.3, SOAP 1.2 Part 1 §2.4, Plan §5.2).
- **Recommendation:** Validate all targeted `mustUnderstand` headers against `_handlers.ContainsKey(header.QName)` in a synchronous pre-pass first; throw `SoapProtocolException(SoapFaultKind.MustUnderstand, ...)` immediately if any are unhandled, and only then execute `ISoapHeaderHandler.HandleAsync` for targeted recognized headers. Add a regression test verifying `0` handler invocations when a known header precedes an unknown targeted mandatory header.

#### **M3. Compatibility Corpus Reporting Overstates WSDL & Serializer Coverage and `compatibility-corpus.md` Is Stale**
- **Locations:**
  - `tests/FastSoap.CompatibilityTests/GoldenCorpusTests.cs:134,136,484-494,535-563`
  - `docs/research/compatibility-corpus.md:27-36`
- **Issues:**
  1. `VerifyWsdlFixtureAsync` (`GoldenCorpusTests.cs:535-563`) only serves and byte-compares `WSDL-ECHO-SOAP11` against FastSoap over HTTP. The remaining 3 entries (`WSDL-PAYMENT-DCS-SOAP11`, `WSDL-PAYMENT-XMLSER-SOAP11`, `WSDL-PAYMENT-DCS-SOAP12`) only parse static files from disk via `ParseStrictXDocument` (`Assert.NotNull(doc.Root)`), yet all 4 are counted as `WsdlPassed = 4` (`GoldenCorpusTests.cs:136`).
  2. `VerifySerializerFragmentAsync` (`GoldenCorpusTests.cs:484-494`) validates `SER-DCS-ECHO` and `SER-XML-ECHO` only by asserting on-disk fixture file contents against hardcoded strings without calling `XmlEssentials` (`XmlSerializer.Deserialize`/`Serialize`), yet counts both in `SerializerPassed = 10` (`GoldenCorpusTests.cs:134`).
  3. `docs/research/compatibility-corpus.md:27-36` still reports FastSoap status as `Untested` (`Passed: 0`, `Failed: 0`, `Excluded: 27`, `Untested: 63`), out of sync with the implemented `FastSoap.CompatibilityTests` suite (where 16/20 positive wire fixtures pass fully, 4/20 are pending/partial, and 27/27 negative fixtures are rejected with 0 invocations).
  4. Note on `NEG-18D` (`GoldenCorpusTests.cs:345-347` & `CompatibilityServerHost.cs:116`): because `CompatibilityServerHost` sets `MaxRequestBytes = 65536`, the ~68 KiB `NEG-18D` header fixture is rejected at `MaxRequestBytes` (`413`) before reaching `MaxHeaderBytes` (`64 KiB`). While `MaxHeaderBytes` is unit-tested in `SoapEnvelopeAndHeaderTests.cs:162-179`, `CompatibilityServerHost` should test `NEG-18D` with `MaxRequestBytes > 68 KiB` so the header-byte quota path itself is exercised end-to-end.
- **Recommendation:** Either serve and verify all 4 WSDL fixtures (and deserialize/serialize `SER-DCS-ECHO`/`SER-XML-ECHO` via `XmlEssentials`) or classify unexercised entries accurately as `pending`/`untested`, and update `docs/research/compatibility-corpus.md` with exact observed counts.

#### **M4. Hot-Path Double Parsing and Per-Request String Allocations**
- **Locations:**
  - `src/FastSoap.AspNetCore/Internal/SoapHttpEndpointHandler.cs:246,275,302`
  - `src/FastSoap.Core/Dispatching/SoapMessageProcessor.cs:253,267`
  - `src/FastSoap.Core/Protocol/SoapContentTypeParser.cs:32,274-282,316,448`
  - `src/FastSoap.Core/Protocol/SoapActionParser.cs:61-73,170`
- **Issues:**
  1. **Duplicate Header Parsing & Lookup:** `SoapHttpEndpointHandler.HandlePostAsync` parses `Content-Type` (`SoapContentTypeParser.Parse` at line `246`) and resolves `SOAPAction` via `_operationByActionLookup` (`line 275`), then calls `SoapMessageProcessor.ProcessAsync` (`line 302`) which re-parses `Content-Type` (`line 253`), re-parses `SOAPAction` (`line 267`), and re-looks up the operation (`SoapEnvelopeReader.cs:287`).
  2. **Per-Request `string` Allocation on `charset=utf-8`:** `SoapContentTypeParser.cs:32` declares `private static ReadOnlySpan<byte> Utf8CharsetUtf8 => "utf-8"u8;` (unused), while `ParseTokenParameterValue` (`line 448`) and `ParseQuotedParameterValue` (`line 427`) allocate a new `string` for every parameter on every request, including `"utf-8"` (`line 316`), instead of comparing the raw UTF-8 span via `Ascii.EqualsIgnoreCase`.
  3. **Transcode-and-Reallocate on `SOAPAction`:** `SoapActionParser.ParseSoap11Action(string?, ...)` (`lines 61-73, 170`) encodes the `string` header to UTF-8 bytes and calls `Encoding.UTF8.GetString(innerUtf8)`, allocating a new `string` on every SOAP 1.1 request. Using `ReadOnlySpan<char>` + `FrozenDictionary<string, ISoapOperation>.AlternateLookup<ReadOnlySpan<char>>` eliminates this allocation entirely.
- **Recommendation:** Pass pre-parsed `SoapContentTypeInfo` (or an internal `ProcessParsedAsync` overload) from `SoapHttpEndpointHandler` to `SoapMessageProcessor`, compare `charset` directly in UTF-8 using `Utf8CharsetUtf8` without allocating a string, and route `SOAPAction` via `ReadOnlySpan<char>` alternate lookup.

---

### Minor

#### **N1. Test Assertion Rule Violation (`Assert.Contains` on XML Output) and Dead Code**
- **Locations:**
  - `tests/FastSoap.ProtocolTests/SoapMessageProcessorAndFixtureTests.cs:76` — `Assert.Contains(expectedFaultCode, bodyText, StringComparison.Ordinal);` violates `AGENTS.md §5` (exact assertions on XML/SOAP output; no `.Contain(...)` shortcuts).
  - `tests/FastSoap.CompatibilityTests/GoldenCorpusTests.cs:452-455` — dead no-op `expectedXml.Replace("TX-BATCH-01-001", "TX-BATCH-01-001", StringComparison.Ordinal)`.
  - `tests/FastSoap.SerializationTests/XmlEssentialsQualificationTests.cs:278` — `Assert.Contains("Amount", ex.Message, StringComparison.Ordinal)` (prefer asserting the exact expected exception message or documenting upstream message instability).

#### **N2. Missing `<GenerateDocumentationFile>true</GenerateDocumentationFile>` in `FastSoap.Core.csproj`**
- **Location:** `src/FastSoap.Core/FastSoap.Core.csproj`
- **Issue:** `FastSoap.Abstractions`, `FastSoap.AspNetCore`, and `FastSoap.Metadata` all enable `<GenerateDocumentationFile>true</GenerateDocumentationFile>`, but `FastSoap.Core.csproj` omits it. Enabling it enforces XML doc completeness (CS1591) under `TreatWarningsAsErrors=true` and emits XML docs for consumers.

#### **N3. `.editorconfig` Contradicts `AGENTS.md §3` on Namespace Declarations**
- **Location:** `.editorconfig:132`
- **Issue:** `.editorconfig:132` configures `csharp_style_namespace_declarations = file_scoped:suggestion`, while `AGENTS.md §3` mandates normal block-scoped namespaces (`namespace FastSoap.Core { ... }`). All `.cs` source files currently conform to `AGENTS.md §3` (block-scoped); `.editorconfig:132` should be updated to `block_scoped:suggestion` to prevent IDE/formatter drift.

#### **N4. Unprotected Fallback Fault Writer in `SoapHttpEndpointHandler.WriteSafeFallbackFaultAsync`**
- **Location:** `src/FastSoap.AspNetCore/Internal/SoapHttpEndpointHandler.cs:622-658`
- **Issue:** Unlike `SoapMessageProcessor.StageFaultResult` (`SoapMessageProcessor.cs:456`), `WriteSafeFallbackFaultAsync` does not catch `ResponseQuotaExceededException` or serialization errors if `options.MaxResponseBytes` is configured smaller than a minimal fault envelope (~200 bytes). Wrapping staging in `try/catch` and falling back to header-only `500` avoids secondary exceptions inside the outer `catch`.

#### **N5. `string.IsNullOrEmpty` Occurrences vs `AGENTS.md §2`**
- **Locations:** `src/FastSoap.Abstractions/SoapQName.cs:132`, `src/FastSoap.AspNetCore/Internal/SoapHttpEndpointHandler.cs:93`, `src/FastSoap.Core/Serialization/SoapFaultWriter.cs:185`, `src/FastSoap.Metadata/SoapMetadataAddressPolicy.cs:156,161,166`, `src/FastSoap.Metadata/SoapSchemaElement.cs:190`, `src/FastSoap.Metadata/SoapSchemaType.cs:246`.
- **Note:** In `SoapHttpEndpointHandler.cs:93` (`?wsdl` with empty vs whitespace value) and `SoapQName.cs:132` (distinguishing `""` empty namespace), `Length == 0` / empty semantics are intentional; in `SoapMetadataAddressPolicy.cs:156,161,166`, `string.IsNullOrWhiteSpace` (or `Length > 0` for non-null `Uri` properties) aligns cleanly with `AGENTS.md §2`.

#### **N6. Minor Ergonomics & Allocation Polish**
- `src/FastSoap.Generators/Emission/SoapContractEmitter.cs:117-120`: Generated `Map<ServiceName>(this IEndpointRouteBuilder endpoints, string pattern)` has no optional `Action<SoapEndpointBuilder>? configure = null` overload, preventing callers from configuring per-endpoint options, header handlers, or static/generated WSDL metadata on generated endpoints.
- `src/FastSoap.Core/Protocol/SoapContentTypeParser.cs:364`: `ParseQuotedParameterValue` allocates `new byte[valueSection.Length]` when a quoted parameter exceeds 512 bytes (bounded by Kestrel's 32 KiB header limit, but rejecting lengths `> maxActionLength` earlier or pooling avoids temporary heap arrays).

---

## 5. Explicit Remaining Limitations & Host / Milestone Blockers

1. **Host Blocker — Real Linked Native AOT Execution (`ADR-001` §3.3 BLOCKER-1):**
   - Verified on this machine that `dotnet publish samples/FastSoap.Sample.NativeAot/FastSoap.Sample.NativeAot.csproj -c Release -r win-x64 --no-restore` cannot run `ilc` / link a native `.exe` because Visual Studio C++ Build Tools (`link.exe`) and Windows SDK libraries are not installed (`Microsoft.NETCore.Native.Windows.targets(142,5): error : Platform linker not found`).
   - Per `AGENTS.md §5` and `ADR-001 §3.3 BLOCKER-1`, M1 cannot be signed off on JIT tests and Roslyn analyzer checks alone: a real linked `FastSoap.Sample.NativeAot` binary must be built (with 0 trim/AOT warnings), launched as a standalone process, and verified against live HTTP and `System.ServiceModel.Http` WCF requests (on a Windows host with MSVC C++ Build Tools or a `linux-x64` CI runner/container with `clang` + `zlib1g-dev`).

2. **Milestone Blocker — Comparative Performance Benchmarks (`ADR-001` §3.3 BLOCKER-3 / `ADR-014`):**
   - `benchmarks/FastSoap.Benchmarks/` exists on disk (outside `FastSoap.slnx`), while `docs/performance.md` and reproducible Release benchmark results comparing FastSoap (JIT + Native AOT) against released `CoreWCF 1.9.2` (with generated invokers verified active) and `SoapCore 1.2.1.18` are not yet recorded/verified.

3. **v0.1 Feature / Interop Limitations Explicitly Tracked:**
   - **Typed Fault Details (`<detail>` / `<s:Detail>`):** `SoapFaultWriter` supports writing validated `DetailXmlUtf8`, but `SoapProtocolException` and `SoapMessageProcessor` do not yet surface typed business fault details from handlers (`SOAP11-TRANSFER-FAULT-TYPED` and `SOAP12-TRANSFER-FAULT-TYPED` currently return reason-only faults and are classified partial).
   - **Outbound SOAP Response Headers:** Inbound headers and `ISoapHeaderHandler` are implemented, but `SoapResponseWriter` does not yet emit outbound `<s:Header>` blocks (`SOAP11-ECHO-HEADER` is classified partial).
   - **Implicit CLR `DataContractSerializer` Namespaces:** `SOAP11-TRANSFER-DCS-DEFAULT` and `SER-DCS-TRANSFER-DEFAULT` remain `pending` (explicit wire DTO namespaces are required in v0.1 per `ADR-003`).
   - **Test Layout:** `FastSoap.AotTests` and `FastSoap.SecurityTests` (Plan §2.2) are not yet separated into standalone projects (security coverage currently resides across `ProtocolTests`, `MetadataTests`, `IntegrationTests`, and `CompatibilityTests`).
