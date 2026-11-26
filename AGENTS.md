# FastSoap Agent Guidelines

This repository implements **FastSoap**, an experimental, AOT-first, UTF-8-native SOAP runtime on ASP.NET Core designed as an alternative to CoreWCF.

Canonical implementation plan: `docs/FastSoap-Implementation-Plan-v2.md`.
Performance reference guide: `docs/references/blazing-fast-dotnet.md` (from [EgorBo/BlazingFastDotnet](https://github.com/EgorBo/BlazingFastDotnet/blob/main/SKILL.md)).

---

## 1. Non-Negotiable Repository Rules

- **Do NOT commit changes.** Never run `git commit`, `git push`, or amend commits. Leave all changes uncommitted in the working tree.
- **No Measurements Out of Thin Air:** Never invent, extrapolate, or claim performance numbers, allocation counts, throughput, or compatibility percentages without running an actual reproducible benchmark or test on this machine and recording the exact command, environment, SDK/runtime, and observed output.
- **Test Against Existing CoreWCF Test Suites for Accuracy:** Validate wire compatibility, SOAP 1.1/1.2 protocol semantics, headers, faults, and serialization accuracy against real CoreWCF test suites and reference serializers (JIT-only oracle tests). Never assume WCF/CoreWCF wire shapes; capture and verify against actual CoreWCF behavior and test cases. Never count skipped or unsupported cases as passing.
- **No Reflection Fallback / No AOT Warning Suppression:**
  - Never suppress a trimming or Native AOT warning. No `UnconditionalSuppressMessage`, no `#pragma` for `IL*` warnings, and no `<NoWarn>` for trim/AOT diagnostics in runtime libraries or generated code.
  - No reflection-based service discovery, `MethodInfo.Invoke`, `DynamicInvoke`, runtime code emission, or reflection serializer fallback in the default runtime.
  - Zero `System.Xml*` references in the core runtime libraries (`FastSoap.Abstractions`, `FastSoap.Core`, `FastSoap.AspNetCore`). Test/oracle/baseline/benchmark projects may reference BCL `System.Xml`, CoreWCF, and SoapCore.
- **Priority Order:** Wire correctness and security -> compatibility -> predictable buffer ownership/lifetimes -> Native AOT -> measured performance.
- **General-Purpose Solutions:** Implement the real algorithm for all valid inputs; never hard-code values to pass specific tests.
- **Clean Up Temporary Files:** Remove any temporary scripts or scratch files created during iteration before finishing.

---

## 2. C# 15 Coding Guidelines

- **Always use C# 15** (`<LangVersion>15</LangVersion>` or latest .NET 11 SDK defaults) for runtime, hosting, sample, and test projects.
  - Exception: Roslyn incremental source generators/analyzers targeting `netstandard2.0` must use the C# 12-compatible subset supported by `netstandard2.0`.
- **Prefer modern C# 15 syntax where clearer:**
  - Collection expressions (`[]`), pattern matching, switch expressions, range expressions, `params` collections, `field` keyword on properties when it removes a manual backing field, `extension` members where appropriate, and UTF-8 string literals (`"..."u8`).
- **Nullability & Argument Validation:**
  - Nullable reference types enabled everywhere (`<Nullable>enable</Nullable>`).
  - Declare variables non-nullable and validate at public/internal entry points.
  - Always use `is null` or `is not null` instead of `== null` or `!= null`.
  - Trust C# null annotations; do not add redundant null checks when the type system guarantees non-null.
  - Always prefer `string.IsNullOrWhiteSpace(...)` over `string.IsNullOrEmpty(...)`.
  - Always prefer `ArgumentException.ThrowIfNullOrWhiteSpace(...)` for string parameter checks.
  - Always prefer `ArgumentNullException.ThrowIfNull(...)` for reference parameter checks.
- **Defense-in-Depth & Fail-First:** Validate all failure modes (quotas, null/empty, bounds, protocol invariants) before executing work or allocating proportional to untrusted input.
- **Avoid Unnecessary Abstractions:** Do not generate interfaces for dependency injection unless strictly necessary.

---

## 3. Formatting & Code Organization

All C# code must conform to `.editorconfig`:

- **Namespaces & Usings:**
  - Prefer **normal block-scoped namespace declarations** (`namespace FastSoap.Core { ... }`), **NOT** file-scoped namespaces.
  - Use single-line `using` directives, sorted with `System.*` first and groups separated.
- **Braces & Returns:**
  - Always insert a **newline before the opening curly brace** (Allman style) of any code block (`if`, `for`, `while`, `foreach`, `using`, `try`, classes, methods, namespaces, etc.).
  - Ensure that the **final `return` statement** of a method is on its own line.
- **Member Organization (`#region`):**
  - Organize class/struct members into `#region` blocks in this exact order:
    1. `#region Fields`
    2. `#region Properties`
    3. `#region Constructors`
    4. `#region Public Methods`
    5. `#region Private Methods` (and `#region Internal Methods` / `#region Protected Methods` between Public and Private when present)
  - Surround `#region` and `#endregion` directives with blank lines.
  - Order members by visibility: `public` first, then `protected`, `internal`, `private`.
  - Order fields, properties, and methods **alphabetically** within their visibility group.
- **Idioms & Names:**
  - Use `nameof(...)` instead of string literals when referring to member names.
  - Use explicit types (`var` disabled per `.editorconfig` where configured).
- **XML Documentation Comments:**
  - Write extensive XML doc comments for all public and significant internal APIs.
  - Include `<example>` and `<code>` blocks where applicable.
  - Only `<param>` tags should be on the same line as their content.
  - Place `<remarks>` as the **last** XML doc tag before the member declaration.
  - Document what exists now, not removed/historical behavior.

---

## 4. High-Performance .NET Rules

Check all hot-path code against `docs/references/blazing-fast-dotnet.md`:

- **Safe C# Only:** Prefer safe, managed C# (`Span<T>`, `ReadOnlySpan<T>`, `Memory<T>`, `IBufferWriter<byte>`, `MemoryMarshal`, `BinaryPrimitives`, `BitOperations`, `SearchValues<byte>`). Avoid `unsafe` code unless strictly justified by an ADR and measured benchmark.
- **Stay in UTF-8:** Search, parse, validate, and format `ReadOnlySpan<byte>` directly using `u8` literals, `SearchValues<byte>`, `Utf8.TryWrite`, and UTF-8 `TryParse`/`TryFormat`. Never round-trip SOAP payloads through `string` or DOM on the hot path.
- **Ordinal Comparisons Only:** Always use `StringComparison.Ordinal` / `StringComparer.Ordinal` (or byte-level `SequenceEqual`). Never use culture-sensitive string comparisons for SOAP actions, namespaces, or QNames.
- **Build-Once, Read-Many Lookups:** Use `FrozenDictionary` / `FrozenSet` for immutable endpoint operation tables and header registries; use `GetAlternateLookup<ReadOnlySpan<char>>()` where string keys are matched from spans.
- **Cache Fields & Avoid Closures:**
  - Cache array/field references in locals inside hot loops to enable JIT bounds-check elimination.
  - Use `static` lambdas and state-passing overloads; avoid closure allocations.
  - Use RVA-backed spans (`static ReadOnlySpan<byte> ... => ...`) for constant protocol tokens.
  - Use `[LoggerMessage]` source-generated logging; guard expensive argument evaluation with `IsEnabled`.
  - Create and reuse `XmlSerializerContext` / options once; never construct options or perform runtime `Type` lookups per request.
- **ASP.NET Core Hot Path:**
  - Use plain `RequestDelegate` for SOAP POST endpoints (bypassing minimal API parameter binding/reflection).
  - Set headers via typed properties (`response.Headers.ContentType = ...`) rather than string indexers.
  - Set `response.ContentLength` when staging responses so HTTP/1.1 avoids chunked framing overhead.
  - Avoid `IHttpContextAccessor`; pass `HttpContext` explicitly.
  - Use `System.Threading.Lock` instead of `object` for locks; avoid shared-state contention and false sharing.
- **Buffer Ownership:**
  - Every rented buffer has an explicit single owner and deterministic `finally`-safe return.
  - Never return a buffer to a different pool; track logical length separately from rented capacity; clear pooled buffers when configured (`ClearPooledBuffers`).
- **Measure First, Profile Real Workloads:**
  - Profile with `dotnet-counters` / `dotnet-trace` / `BenchmarkDotNet` before optimizing.
  - Verify 0-byte allocation claims with `GC.GetAllocatedBytesForCurrentThread()` tests.
  - Never use `AggressiveOptimization` blindly or overuse `AggressiveInlining` without measured evidence.

---

## 5. Testing & Verification Rules

- **Always specify `-c Release` or `-c Debug`** on every `dotnet` build/test/run command.
- **No Mocking:** Do not use mocking frameworks in tests. Use real implementations, explicit fakes/stubs against frozen contracts, real Kestrel for HTTP/AOT/load tests, and deterministic tracking pools for buffer ownership tests.
- **No Arrange/Act/Assert Comments:** Do not emit `// Arrange`, `// Act`, or `// Assert` comments.
- **Exact Assertions:**
  - Assert complete expected values (exact string, exact byte sequence, exact infoset comparison, exact exception type/message).
  - Do not use `.Contain(...)`, `.StartWith(...)`, `.EndWith(...)`, or length-only checks on XML/SOAP output.
  - Prefer `.NotBeNullOrWhiteSpace()` over `.NotBeNullOrEmpty()`.
- **CoreWCF Test Suite Accuracy:**
  - Maintain a versioned compatibility corpus (`docs/research/compatibility-corpus.md` and `tests/fixtures/`) referencing real CoreWCF test suites and WCF/BCL serializer outputs.
  - Report passed, failed, excluded, and untested counts separately. Never manipulate denominators or relax fixture expectations to get a green build.
- **No Coverage-Only Tests:** Every test must verify real caller-visible behavior or an internal precondition (in `*InternalsTests`).
- **Real Native AOT Verification:** Publish and execute the actual Native AOT binary and send real HTTP requests against it; a JIT test or publish-only check is insufficient.

---

## 6. Windows Build & Shell Notes

- **NEVER redirect output to `nul` on Windows.** Use `> $null` in PowerShell or `> NUL` in cmd.
- Always specify `-c Release` or `-c Debug` on `dotnet` commands.
- Keep work strictly within your assigned files/work package when running alongside other agents.
