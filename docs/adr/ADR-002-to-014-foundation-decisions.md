# ADR-002 through ADR-014: Frozen Foundation Architecture Decisions

**Status:** Accepted (A0 Foundation Freeze)  
**Date:** 2026-11-26  
**Prerequisite:** [ADR-001: Runtime Architecture Selection & Gate 0 Go/No-Go Decision](ADR-001-runtime-architecture.md)  
**Canonical Specification:** [FastSoap-Implementation-Plan-v2.md](../FastSoap-Implementation-Plan-v2.md)

---

## ADR-002: .NET 10 LTS Baseline, SDK `11.0.100` Pin, and Exact `XmlEssentials` `1.0.0` Pin

- **Decision:**
  - Pin the repository toolchain in `global.json` to `.NET SDK 11.0.100` (`rollForward: latestFeature`, `allowPrerelease: false`) to support C# 15 (`<LangVersion>15</LangVersion>`) cleanly on GA tooling.
  - Target `net10.0` (LTS) across all runtime, hosting, sample, test, oracle, and benchmark projects (`Directory.Build.props`) so all runtime measurements and compatibility checks execute against the same `.NET 10` (`10.0.15`) platform.
  - Keep `FastSoap.Generators` targeting `netstandard2.0` with the C# 12 language subset required by Roslyn analyzers.
  - Pin `XmlEssentials` to exact NuGet version `1.0.0` (commit `44b8a0d90bd62c7f83e94f2b4c63f87defbd4134`) in `Directory.Packages.props` (`ManagePackageVersionsCentrally=true`).
- **Consequences:** Eliminates cross-TFM drift observed during R0 spikes and protects the runtime from upstream `v1.0.1` breaking metadata API changes until explicitly re-qualified.

---

## ADR-003: Explicit XML Contexts, Wire DTOs, and `SoapWireContractAttribute` Generator Mapping

- **Decision:**
  - Require explicit authored wire DTOs (`[XmlRoot]`, `[XmlElement(..., Namespace = "...")]`) and authored `XmlSerializerContext` subclasses (`[XmlSerializable<T>]`) in v0.1 rather than relying on cross-generator visibility between FastSoap's generator and `XmlEssentials`'s generator during the same compilation.
  - Freeze the compile-time contract attributes in `FastSoap.Abstractions`:
    - `[SoapService(Name = ..., Namespace = ...)]` on the service contract interface/class.
    - `[SoapOperation(Name = ..., Action = ..., ReplyAction = ..., RequestName = ..., ResponseName = ...)]` on operation methods.
    - `[SoapWireContract(typeof(MyXmlContext))]` on the service contract (or method) and `[SoapWireContract(RequestType = ..., RequestMemberName = ..., ResponseType = ..., ResponseMemberName = ...)]` on each operation method, with optional `RequestTypeInfoPropertyName` / `ResponseTypeInfoPropertyName` overrides.
  - Generated code emits static `MapOperation<TRequestWire, TResponseWire>(...)` calls over the exact same manual builder API, referencing `MyXmlContext.Default.<TypeInfo>` directly with zero runtime reflection.
- **Consequences:** Prevents silent `DataContractSerializer` wire-shape mismatches, respects `XmlEssentials 1.0.0` wildcard-namespace constraints (requiring explicit `Namespace` on `[XmlElement]`), and guarantees Native AOT safety.

---

## ADR-004: Bounded Buffered Request Intake First

- **Decision:**
  - Consume `HttpRequest.BodyReader` incrementally into a bounded `SoapRequestBuffer` rented from an injectable `ArrayPool<byte>`, growing geometrically up to `SoapEndpointOptions.MaxRequestBytes` (default 1 MiB).
  - Check known `Content-Length` early before reading; enforce an independent cumulative byte counter for chunked/unknown-length bodies; advance the pipe in `finally` without retaining pipe sequences past `AdvanceTo`.
  - Parse the complete owned UTF-8 buffer synchronously with `Utf8XmlReader` before crossing any `await`.
- **Consequences:** Prevents pipe backpressure deadlocks, guarantees full XML document validation before service invocation, and enforces strict single-owner buffer lifetimes.

---

## ADR-005: Stage Bounded Responses and Faults Before HTTP Commit

- **Decision:**
  - Stage complete SOAP response envelopes in a bounded pooled `IBufferWriter<byte>` up to `SoapEndpointOptions.MaxResponseBytes` (default 2 MiB) using `Utf8XmlWriter` (`OmitXmlDeclaration = true`).
  - Stage SOAP fault envelopes in a separate small bounded fault buffer.
  - Set `Response.StatusCode`, typed `Response.Headers.ContentType`, and `Response.ContentLength` only after serialization succeeds and passes quota checks, then copy staged bytes to `HttpResponse.BodyWriter` using `BuffersExtensions.Write(response.BodyWriter, stagedMemory.Span)` (never `GetSpan(fullLength)`).
  - Never attempt to write a SOAP fault if `HttpResponse.HasStarted` is `true` or `RequestAborted.IsCancellationRequested` is `true`.
- **Consequences:** Serialization failures or quota overflows after business execution never corrupt the wire with truncated HTTP 200 XML; HTTP/1.1 avoids chunked framing overhead.

---

## ADR-006: Static Typed Operation Invokers & Immutable Operation Tables

- **Decision:**
  - Dispatch operations using `SoapOperationHandler<TRequest, TResponse>` (`ValueTask<TResponse>`) stored inside generic `SoapOperation<TRequest, TResponse>` entries held in one immutable `FrozenDictionary<string, ISoapOperation>` per endpoint using `StringComparer.Ordinal`.
  - Secondary Body-QName routing is built only when `AllowMissingSoap11Action = true` or SOAP 1.2 actionless routing is active, and rejects ambiguous QNames or duplicate actions at endpoint startup.
  - Zero `object[]`, `MethodInfo.Invoke`, `DynamicInvoke`, or runtime service scanning.
- **Consequences:** Zero reflection and predictable AOT codegen on the hot path.

---

## ADR-007: Strict Document/Literal Wire Contract, Exact QName Matching, and XML 1.0 Profile

- **Decision:**
  - Support document/literal wrapped and explicit wire DTOs only; reject RPC/encoded (`soap:encodingStyle` attributes on `Envelope` or `Body`) and SOAP encoding.
  - Match envelope, header, and body element names by expanded QName (`SoapQName.Matches` comparing pre-encoded UTF-8 `NamespaceUriUtf8` and `LocalNameUtf8` byte-for-byte), never by prefix spelling or case-insensitive matching.
  - Enforce XML 1.0 only (`XmlVersion.Version10`), prohibit DTDs/entities, prohibit processing instructions (`<?...?>`), reject invalid UTF-8 and non-UTF-8 charsets/declarations, and enforce `MaxXmlDepth` (64), `MaxAttributesPerElement` (64), `MaxHeaderCount` (32), `MaxHeaderBytes` (64 KiB), and `MaxActionLength` (1024).
  - Validate `SoapQName` and `SoapOperationDescriptor` invariants eagerly at construction and `SoapEndpointOptions.Validate()` at startup.
- **Consequences:** Rejects malformed, ambiguous, or hostile payloads before business invocation (`0` service calls on invalid input).

---

## ADR-008: ASP.NET Core Hosting, Plain `RequestDelegate`, and Standard Authorization

- **Decision:**
  - Map SOAP POST endpoints via explicit ASP.NET Core `RequestDelegate` (`MapSoap`), bypassing minimal API parameter binding and reflection.
  - Rely exclusively on ASP.NET Core middleware (`UseAuthentication`, `UseAuthorization`, `.RequireAuthorization()`, rate limiting, forwarded headers, Kestrel limits) for transport security and identity.
  - Never derive security identity implicitly from SOAP headers; optional `ISoapHeaderHandler` implementations are explicitly registered by `SoapQName` and execute after full parse and before operation invocation using owned `SoapHeaderValue` payloads.
- **Consequences:** Avoids bespoke HTTP auth/TLS logic and integrates cleanly with Native AOT ASP.NET Core slim builder.

---

## ADR-009: Explicit Supported Schema Subset and Contract-First Static WSDL 1.1 / XSD

- **Decision:**
  - Keep `EnableMetadata = false` by default. When enabled, serve verified static or supported-subset WSDL 1.1 / XSD artifacts over GET `?wsdl` / configured schema paths without runtime reflection, external network fetches, or trusting arbitrary `Host` / `X-Forwarded-Host` headers.
  - Keep metadata generation and schema tooling in `FastSoap.Metadata` outside the hot request path and outside `FastSoap.Abstractions`.
- **Consequences:** Prevents inaccurate metadata advertising and eliminates SSRF / host-header injection surfaces.

---

## ADR-010: Protocol Core Independent of Hosting

- **Decision:**
  - Keep `FastSoap.Abstractions` free of `Microsoft.AspNetCore.App`, `XmlEssentials`, Roslyn, and `System.Xml*` dependencies.
  - Keep `FastSoap.Core` independent of ASP.NET Core hosting (`HttpContext` adaptation lives solely in `FastSoap.AspNetCore`) so protocol, buffer ownership, and serialization logic can be tested deterministically without Kestrel.
- **Consequences:** Clean dependency DAG (`AspNetCore -> Core -> Abstractions`) and fast isolated unit/protocol testing.

---

## ADR-011: Internal XML Backend Seam & Zero `System.Xml*` Runtime Dependency

- **Decision:**
  - Isolate all `CloudNimble.Text.Xml` (`XmlEssentials 1.0.0`) calls behind narrow internal adapters in `FastSoap.Core`. Do not expose `Utf8XmlReader`, `Utf8XmlWriter`, or `XmlTypeInfo<T>` in `FastSoap.Abstractions`.
  - Enforce zero `System.Xml*` assembly references in `FastSoap.Abstractions`, `FastSoap.Core`, and `FastSoap.AspNetCore` via automated `PEReader`/`MetadataReader` unit tests.
  - Configure `XmlSerializerOptions` once per context with `AllowDuplicateProperties = false`, `UnmappedMemberHandling = XmlUnmappedMemberHandling.Disallow`, and `OmitXmlDeclaration = true`, and configure `XmlReaderOptions` explicitly with `MaxDocumentByteCount`, `MaxDepth`, `MaxAttributeCount`, `MaxNameByteCount`, and `AllowMultipleRoots = false`.
- **Consequences:** Contains upstream `XmlEssentials` API churn and guarantees zero accidental BCL `System.Xml` coupling in shipping binaries.

---

## ADR-012: Independent Compatibility Corpus & JIT-Only Reference Oracles

- **Decision:**
  - Maintain versioned golden wire fixtures in `tests/fixtures/` (`manifest.json`, SHA-256 verified) captured from live `CoreWCF` and `System.ServiceModel.Http` runs under JIT.
  - Never allow `CoreWCF.*`, `SoapCore`, `System.ServiceModel.*`, or `System.Xml*` packages into runtime libraries (`src/FastSoap.Abstractions`, `src/FastSoap.Core`, `src/FastSoap.AspNetCore`).
  - Report supported, failed, excluded, and untested counts separately with zero denominator manipulation; verify real linked Native AOT HTTP executables as a mandatory M1 release gate.
- **Consequences:** Honest, reproducible wire compatibility tracking without polluting the AOT runtime.

---

## ADR-013: Explicit Ownership, Capacity Budgets, and Clearing Policy at Every I/O Boundary

- **Decision:**
  - Inject `ArrayPool<byte>` into pooled request and response buffers (defaulting to `ArrayPool<byte>.Shared`) so tests verify exact rental accounting, peak growth memory, foreign-return rejection, and exception-path return via deterministic tracking pools.
  - Track logical written byte length separately from rented buffer capacity; clear used/rented buffers on return when `SoapEndpointOptions.ClearPooledBuffers` is `true` (default).
  - Never transfer a rented buffer without transferring its deterministic `finally`-safe disposal responsibility.
- **Consequences:** Eliminates buffer leaks and cross-pool corruption observed in legacy stacks.

---

## ADR-014: Comparative Performance Baselines

- **Decision:**
  - Benchmark FastSoap (JIT manual, JIT generated, and Native AOT) on unified `net10.0` Release against released `CoreWCF 1.9.2` (both default and source-generated invokers verified active) and `SoapCore 1.2.1.18` on identical R0-G wire contracts.
  - Do not claim performance numbers without reproducible `BenchmarkDotNet` and real Kestrel HTTP load measurements on the target machine.
- **Consequences:** Fair, apples-to-apples comparisons against tuned released baselines.

---

## Frozen SOAP 1.1 & SOAP 1.2 HTTP Status and Fault Matrix (Gate 0 Resolution)

This matrix resolves `ADR-001` §3.1 item 3 and governs `SoapProtocolException` / `SoapFault` mapping across `FastSoap.Core` and `FastSoap.AspNetCore`.

| Condition / Failure Category | Service Invoked? | SOAP 1.1 HTTP Status & Body | SOAP 1.2 HTTP Status & Body | Notes |
|---|---|---|---|---|
| **Success** | Yes (1x) | `200 OK`, `text/xml; charset=utf-8`, SOAP 1.1 Envelope | `200 OK`, `application/soap+xml; charset=utf-8`, SOAP 1.2 Envelope | `Content-Length` set from staged buffer. |
| **ASP.NET AuthN / AuthZ Failure** | No (0x) | `401` / `403` (standard ASP.NET Core challenge/forbid) | `401` / `403` (standard ASP.NET Core challenge/forbid) | Handled before reading request body. |
| **Non-POST Method (except enabled GET `?wsdl`)** | No (0x) | `405 Method Not Allowed` (or `404` if unmapped) | `405 Method Not Allowed` | Standard ASP.NET routing / endpoint policy. |
| **Unsupported Media Type / Non-UTF-8 Charset / Unsupported Content-Encoding** | No (0x) | `415 Unsupported Media Type` (empty/safe HTTP error) | `415 Unsupported Media Type` (empty/safe HTTP error) | Includes `charset=utf-16` (`NEG-09`) and SOAP 1.2 `application/soap+xml` sent to SOAP 1.1-only endpoint when rejected at media-type gate. |
| **Request Exceeds `MaxRequestBytes`** | No (0x) | `413 Payload Too Large` | `413 Payload Too Large` | Checked on known `Content-Length` and incrementally on `BodyReader` (`NEG-08`). |
| **Malformed HTTP Metadata / Malformed or Oversized `SOAPAction` / Duplicate `action` Parameter** | No (0x) | `400 Bad Request` | `400 Bad Request` | Safe plain/empty HTTP error; never echo untrusted header text. |
| **Pre-Envelope XML Error (Invalid UTF-8, DTD/XXE, Entity Expansion, XML 1.1, PI Before Root, Trailing Root, Non-`Envelope` Root)** | No (0x) | `400 Bad Request` (`text/plain; charset=utf-8` or empty) | `400 Bad Request` (`text/plain; charset=utf-8` or empty) | Covers `NEG-01`, `NEG-02`, `NEG-03A`, `NEG-03B`, `NEG-10`, `NEG-11`; distinguishes non-`Envelope` root (`400`) from wrong-namespace `Envelope` (`VersionMismatch`). |
| **Envelope Root `Envelope` with Unrecognized Namespace (`VersionMismatch`)** | No (0x) | `500 Internal Server Error`, `text/xml; charset=utf-8`, SOAP 1.1 `<faultcode>s:VersionMismatch</faultcode>` | `500 Internal Server Error`, `application/soap+xml; charset=utf-8`, SOAP 1.2 `<s:Value>s:VersionMismatch</s:Value>` | Emitted using the endpoint's active/negotiated SOAP version (`NEG-13`); if endpoint allows only SOAP 1.1 and HTTP media type was `text/xml`, respond with SOAP 1.1 `s:VersionMismatch`. |
| **Malformed Envelope Grammar / `encodingStyle` / Missing `SOAPAction` (strict) / Unknown Action / Action-Body QName Mismatch / Deserialization Schema Violation** | No (0x) | `500 Internal Server Error`, `text/xml; charset=utf-8`, SOAP 1.1 `<faultcode>s:Client</faultcode>` | `400 Bad Request`, `application/soap+xml; charset=utf-8`, SOAP 1.2 `<s:Value>s:Sender</s:Value>` | Resolves W3C SOAP 1.2 Part 2 §7.5.1.1 HTTP binding (`Sender` -> HTTP `400`) vs SOAP 1.1 HTTP binding (`Client` -> HTTP `500`). Covers `NEG-04`, `NEG-05`, `NEG-07`, `NEG-12`. |
| **Unprocessed Targeted Mandatory Header (`mustUnderstand`)** | No (0x) | `500 Internal Server Error`, `text/xml; charset=utf-8`, `<faultcode>s:MustUnderstand</faultcode>` | `500 Internal Server Error`, `application/soap+xml; charset=utf-8`, `<s:Value>s:MustUnderstand</s:Value>` + SOAP 1.2 `NotUnderstood` header | Targeted by SOAP 1.1 `actor` / SOAP 1.2 `role` (`NEG-06A`, `NEG-06B`); non-targeted actors/roles do not fault. |
| **Explicitly Mapped Safe Business / FaultContract Error (`Sender` Fault)** | Yes (1x) | `500 Internal Server Error`, `text/xml; charset=utf-8`, `<faultcode>s:Client</faultcode>` + optional safe `<detail>` | `500 Internal Server Error`, `application/soap+xml; charset=utf-8`, `<s:Value>s:Sender</s:Value>` + optional safe `<s:Detail>` | Matches CoreWCF `SOAP11-TRANSFER-FAULT-TYPED` and `SOAP12-TRANSFER-FAULT-TYPED` (`FaultException<T>` -> HTTP `500`) for `System.ServiceModel.Http` WCF client interoperability. |
| **Unhandled Server Exception / Response Staging Quota Overflow (Before Commit)** | Yes / N/A | `500 Internal Server Error`, `text/xml; charset=utf-8`, `<faultcode>s:Server</faultcode>` (safe sanitized reason) | `500 Internal Server Error`, `application/soap+xml; charset=utf-8`, `<s:Value>s:Receiver</s:Value>` (safe sanitized reason) | Never emits raw exception messages, stack traces, or internal details on the wire. |
| **Client Disconnect / `RequestAborted` / Fault After `Response.HasStarted`** | N/A | Abort / log only; no fault body write | Abort / log only; no fault body write | Prevents protocol corruption on partially committed responses. |
