# C0 Qualification Report: `CloudNimble/XmlEssentials`

**Date:** 2026-11-26  
**Status:** Qualified with documented upstream guardrails (pinned at NuGet `1.0.0`)  
**Machine SDKs:** `.NET 11.0.100` (default), `.NET 10.0.100-rc.1.25451.107`, `.NET 8.0.428`, `.NET 6.0.321`

## 1. Package Version, Commit Pin, License, and Target Frameworks

| Property | Verified Value |
|---|---|
| NuGet Package ID | `XmlEssentials` |
| Latest Published NuGet Version | `1.0.0` (`api.nuget.org/v3-flatcontainer/xmlessentials/index.json`: `["1.0.0-preview.1", "1.0.0"]`) |
| Pinned Upstream Commit SHA (`v1.0.0`) | `44b8a0d90bd62c7f83e94f2b4c63f87defbd4134` (tagged `v1.0.0`, released `2026-11-25T20:23:00Z`) |
| Newer Upstream Tag (Not on NuGet) | `v1.0.1` (`f7bd3a3376db16b5d4151d0ac2fae26da001544e`, `2026-11-26T18:06:53Z`) — `nuget.org/packages/XmlEssentials/1.0.1` returned HTTP 404 |
| License | MIT (`<license type="expression">MIT</license>`, `LICENSE` in `.nupkg`) |
| NuGet Dependencies | None (`<group targetFramework="net10.0" />`, `<group targetFramework="net11.0" />`) |
| Bundled Analyzer | `analyzers/dotnet/cs/CloudNimble.XmlEssentials.SourceGeneration.dll` (`157,696` bytes, `netstandard2.0`) |
| Target Frameworks | `net10.0`, `net11.0` |
| Assembly References (`lib/net10.0/CloudNimble.XmlEssentials.dll`) | `System.Collections`, `System.Collections.Concurrent`, `System.Collections.Immutable`, `System.IO.Pipelines`, `System.Memory`, `System.Runtime`, `System.Runtime.InteropServices`, `System.Runtime.Numerics`, `System.Threading` (**zero `System.Xml*` references**) |

### SDK Compatibility Note

- `CloudNimble.XmlEssentials.csproj` targets `net11.0;net10.0` (`<LangVersion>latest</LangVersion>`).
- In `experiments/R0C-GreenfieldSpike`, targeting `net10.0` with `<LangVersion>15</LangVersion>` restored `XmlEssentials` `1.0.0` and `Microsoft.DotNet.ILCompiler` `10.0.15` cleanly under the `.NET 11.0.100` SDK.
- FastSoap's `Directory.Build.props` / `global.json` should pin `.NET 11.0.100` (or verify whether `.NET 10.0.100-rc.1` supports `<LangVersion>15</LangVersion>`, as C# 15 language support requires the .NET 11 SDK).

## 2. Exact Attribute & Context Syntax (`XmlEssentials` `1.0.0`)

Namespaces:
- `CloudNimble.Text.Xml` (`Utf8XmlReader`, `Utf8XmlWriter`, `XmlSerializer`, `XmlSerializerOptions`, `XmlReaderOptions`, `XmlWriterOptions`, `XmlException`, `XmlTokenType`, `XmlVersion`)
- `CloudNimble.Text.Xml.Serialization` (`XmlRootAttribute`, `XmlElementAttribute`, `XmlSerializableAttribute<T>`, `XmlSerializerContext`, `XmlSourceGenerationOptionsAttribute`, `XmlTypeInfo<T>`)

Verified attribute signatures from `v1.0.0` source:
- `[XmlRoot(ElementName = "Transfer", Namespace = "urn:payments")]`
  - `AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, AllowMultiple = false`
  - Properties: `string? ElementName`, `string? Namespace`
- `[XmlElement(ElementName = "request", Namespace = "urn:payments", Order = 0, IsNullable = false)]`
  - `AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true`
  - Properties: `string? ElementName`, `bool IsNullable`, `string? Namespace`, `int Order = -1` (note: in `v1.0.1`, `Order` semantics change so `0` is unset and `-1` sorts before `0`), `virtual Type? Type => null`
  - Generic sibling `[XmlElement<T>]` is also available.
- `[XmlSerializable<T>]`
  - `AttributeTargets.Class, AllowMultiple = true`
  - Properties: `XmlSourceGenerationMode GenerationMode`, `Type Type => typeof(T)`, `string? TypeInfoPropertyName`
- `public partial class SpikeXmlContext : XmlSerializerContext`
  - Generates `Default` singleton and typed `XmlTypeInfo<T>` properties (e.g., `context.TransferRequestWire`).
- Reflection-free enforcement:
  - Setting `<RuntimeHostConfigurationOption Include="CloudNimble.Text.Xml.XmlSerializer.IsReflectionEnabledByDefault" Value="false" Trim="true" />` disables reflection fallback at compile/trim time.

## 3. Protocol, Security, and Serialization Qualification

| Area | Observed Behavior in `XmlEssentials` `1.0.0` | FastSoap Action / Guardrail Required |
|---|---|---|
| **Namespace Scope (`Read`)** | `Utf8XmlReader` maintains an inline 8-slot (heap-growing) namespace stack (`ApplyNamespaces`), resolves prefixes and default namespaces, and exposes zero-allocation `NamespaceUriEquals(ReadOnlySpan<byte>)` and `LocalNameEquals(ReadOnlySpan<byte>)`. `XmlSerializer.Deserialize(ref reader, xmlTypeInfo)` reads from the reader's live position inside the envelope's inherited namespace scope (verified with both `xmlns="urn:payments"` default namespace and `xmlns:p="urn:payments"` prefixed elements). | Keep `Utf8XmlReader` synchronous from envelope root through `XmlSerializer.Deserialize(ref reader, typeInfo)` and trailing `</s:Body>`/`</s:Envelope>` validation. |
| **Namespace Scope (`Write`)** | `XmlSerializer.Serialize(writer, value, xmlTypeInfo)` writes into an already-open `<s:Body>` (`writer.CurrentDepth > 0`) via `WriteStartRoot` -> `WriteStartElement(utf8LocalName, _rootNamespaceUri)`. Confirmed via live HTTP output that `Utf8XmlWriter` declares `xmlns="urn:payments"` once on `<TransferResponse>` and omits redundant `xmlns` on child elements (`<TransferResult>`, `<TransactionId>`, `<Successful>`). | Always set `OmitXmlDeclaration = true` on `XmlWriterOptions` when writing inside `<s:Body>`. |
| **Root Namespace Check Limitation** | In `XmlTypeInfo.CheckRoot`, `if (_rootNamespaceUri.Length > 0 && !reader.NamespaceUriEquals(_rootNamespaceUri))` skips namespace validation when `_rootNamespaceUri.Length == 0`. | `SoapEnvelopeReader` must explicitly verify both `reader.LocalNameEquals(...)` and `reader.NamespaceUriEquals(...)` (including empty namespace when expected) before invoking `Deserialize`. |
| **Member Namespace Wildcard Limitation** | In `XmlBoundProperty.Matches` (`XmlObjectBinder.cs:155`), an element property with `Namespace = null` (`Utf8NamespaceUri.Length == 0` and `NoNamespace == false`) matches that local name in **any** namespace unless claimed by a sibling property. | Wire DTOs must always declare explicit `Namespace = "..."` (or `Namespace = ""` for unqualified no-namespace elements, which sets `NoNamespace = true`). |
| **Duplicate Elements Limitation** | `XmlSerializerOptions.AllowDuplicateProperties` defaults to `true`, and `XmlSourceGenerationOptionsAttribute` does **not** expose `AllowDuplicateProperties` or `UnmappedMemberHandling`. | Instantiate the generated `XmlSerializerContext` once with `new MyContext(new XmlSerializerOptions { AllowDuplicateProperties = false, UnmappedMemberHandling = XmlUnmappedMemberHandling.Disallow, OmitXmlDeclaration = true })`. Verified that `XmlMetadataServices.CanUseSerializeHandler` keeps the generated write fast path enabled with those options. |
| **Reader Quotas** | `XmlReaderOptions` defaults: `MaxDepth = 64`, `MaxAttributeCount = 1000`, `MaxNameByteCount = 1024`, `MaxTextByteCount = 16,777,216`, `MaxCommentByteCount = 1,048,576`, **`MaxDocumentByteCount = 0` (unbounded)**. | FastSoap must always set `MaxDocumentByteCount` explicitly (plus tighter `MaxAttributeCount`, `MaxDepth`, `MaxTextByteCount`). |
| **Invalid UTF-8 & Encodings** | `Utf8XmlReader` rejects UTF-16/UTF-32 BOMs and `<`+`\0` prefixes, rejects `<?xml ... encoding="..."?>` other than `utf-8`/`utf8`, and throws `XmlException("Invalid UTF-8.")` via `Rune.DecodeFromUtf8` and `Utf8.IsValid` (verified with `0xFF 0xFE` bytes over HTTP -> HTTP 400). | Catch `XmlException` at the protocol boundary and return HTTP 400 without invoking the service. |
| **DTD Rejection** | `TryReadMarkupDeclaration` unconditionally throws `XmlException("DTD is not allowed.")` for `<!DOCTYPE`, `<!ENTITY`, `<!ELEMENT`, `<!ATTLIST`, and `<!NOTATION`. Verified over HTTP (`<!DOCTYPE foo>...` -> HTTP 400). | Rely on built-in rejection; verify in security tests. |
| **XML 1.1 & Processing Instructions** | `Utf8XmlReader` accepts `<?xml version="1.1"...?>` (`XmlVersion.Version11`) and emits `XmlTokenType.ProcessingInstruction` for `<?target ...?>`. SOAP 1.1/1.2 requires XML 1.0 only and prohibits PIs. | Envelope reader loop must reject `reader.Version != XmlVersion.Version10` and `XmlTokenType.ProcessingInstruction` (verified over HTTP -> HTTP 400). |
| **Writer Ownership & Flush** | `Utf8XmlWriter` writes to `IBufferWriter<byte>` (advances on `Flush()` / `FlushAsync()`) or `Stream` (rents/returns `ArrayPool<byte>.Shared` buffer, never disposes caller stream/buffer writer). `Dispose()` calls `Flush()` and throws `XmlException("The document has unclosed elements.")` if `_depth > 0` and `!SkipValidation`. `Reset(IBufferWriter<byte>)` enables reuse. | Stage into a bounded pooled `IBufferWriter<byte>`, call `writer.Flush()`, verify response quota, copy to `HttpResponse.BodyWriter`, and return staged memory in `finally`. |
| **Native AOT & Trimming** | `R0C-GreenfieldSpike` compiled with `TreatWarningsAsErrors=true`, `IsAotCompatible=true`, `PublishAot=true`, and `XmlSerializer.IsReflectionEnabledByDefault=false`. Both Roslyn analyzers and `ilc.exe` native codegen (`Generating native code`, producing `56,973,102`-byte `.obj`) completed with **0 trim/AOT warnings**. Final native EXE linking was blocked on this host only by missing Visual Studio C++ `link.exe` / Windows SDK libs. | Ready for Native AOT once built on a host/CI runner with MSVC C++ Build Tools (Windows) or `clang` (Linux). |

## 4. Qualification Verdict

**Accept `XmlEssentials` `1.0.0` (commit `44b8a0d90bd62c7f83e94f2b4c63f87defbd4134`)** as the pinned XML backend for FastSoap, wrapped behind an internal adapter that enforces:
1. `MaxDocumentByteCount` and tight `XmlReaderOptions` quotas,
2. XML 1.0-only (`XmlVersion.Version10`) and PI rejection,
3. Explicit expanded QName (`LocalNameEquals` + `NamespaceUriEquals`) validation on envelope/body/root elements,
4. Explicit `[XmlElement(..., Namespace = "...")]` (or `Namespace = ""`) on wire DTOs,
5. `AllowDuplicateProperties = false` and `UnmappedMemberHandling = XmlUnmappedMemberHandling.Disallow` on the shared `XmlSerializerOptions`.
