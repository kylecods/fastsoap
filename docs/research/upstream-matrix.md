# Upstream Research Matrix (R0-A)

**Snapshot Date:** 2026-11-26  
**Assigned Track:** R0-A (CoreWCF & `dotnet/runtime` upstream audit)  
**Verification Basis:** Live GitHub REST API (`api.github.com`), raw repository files (`raw.githubusercontent.com`), NuGet v3 API (`api.nuget.org`), and NuGet package manifests inspected on 2026-11-26.

---

## 1. Upstream Repositories & Released Packages

| Project / Package | Version / Tag / Branch | Exact Commit SHA | License | Status / Notes |
|---|---|---|---|---|
| `CoreWCF/CoreWCF` (`main`) | `main` | `aef18198b56b608d6940ddb2b8c0050a6bfe0862` | MIT (`LICENSE`) | Active upstream default branch (commit date: 2026-11-19) |
| `CoreWCF/CoreWCF` (`v1.9.2`) | `v1.9.2` | `62a222a59084af52a94afccac37d842b930ea46b` | MIT | Latest released tag (published 2026-11-21) |
| NuGet `CoreWCF.Http` | `1.9.2` | `62a222a59084af52a94afccac37d842b930ea46b` | MIT | Targets `netstandard2.0`; depends on `CoreWCF.Primitives` `1.9.2`, `System.Web.Services.Description` `8.1.2`, `Microsoft.IdentityModel.*` `8.19.1`, `System.DirectoryServices*` `8.0.x`, `System.Security.Cryptography.Xml` `8.0.5` |
| NuGet `CoreWCF.Primitives` | `1.9.2` | `62a222a59084af52a94afccac37d842b930ea46b` | MIT | Targets `netstandard2.0`; bundles `CoreWCF.BuildTools.Roslyn3.11.dll` & `CoreWCF.BuildTools.Roslyn4.0.dll` (`analyzers/dotnet/roslyn4.8/cs/`) and `build/CoreWCF.Primitives.targets` |
| NuGet `CoreWCF.Templates` | `1.9.2` | `62a222a59084af52a94afccac37d842b930ea46b` | MIT | Latest released template package |
| NuGet `CoreWCF.ConfigurationManager` | `1.9.2` | `62a222a59084af52a94afccac37d842b930ea46b` | MIT | Latest released config package |
| `dotnet/runtime` (`main`) | `main` | `43a20b31c968e6936040727475224d230e1b96f7` | MIT | Inspected 2026-11-26 |

---

## 2. CoreWCF Upstream Issues, PRs, and Discussions

| Reference | Title | State | Author / Dates | Exact Commit SHAs | Key Findings & FastSoap Implications |
|---|---|---|---|---|---|
| [CoreWCF #1766](https://github.com/CoreWCF/CoreWCF/pull/1766) | An AOT-safe, source-generated DataContractSerializer | **Open (Draft)**, unmerged | `g7ed6e` · Created 2026-08-09, Updated 2026-08-09 | Head (`g7ed6e:feat/aot-datacontractserializer`): `878e002ea6854d43f9b5131272872b23d642e263` <br> Base (`CoreWCF:main`): `6e2cce88349fe41056fe8afda0dbbdf98d54bb00` | 39 commits, 179 changed files (`+21220/-9`). Adds `CoreWCF.DataContractSerialization` + Roslyn generator (`[DataContractSerializable]`) behind `AppContext` switch (`CoreWCF.Serialization.UseGeneratedDataContractSerializers`, defaulting to `true` when `!RuntimeFeature.IsDynamicCodeSupported`). Covers **operation body only** via `PartInfo` seam; explicitly leaves `PrimitiveOperationFormatter`, `FaultContractInfo`, `Message`/`MessageFault`/`MessageHeader`/`AddressHeader`, and `Security/` (`SctClaimSerializer`, WS-Trust) on reflection `DataContractSerializer`. Author-reported numbers (not independently executed): 81/86 `WriteObject` byte-identical, 79/86 `ReadObject` round-trip, 5 skipped exclusions (3 runtime `[KnownType("Method")]`, 1 non-public member, 1 missing `[DataContract]`), 2 write-only (no accessible parameterless ctor), and `AotSmokeTest` running over HTTP with **339 trim/AOT warnings** (207 `IL3050`, 72 `IL2026`, 56 reflection, 4 `IL3054`) and requiring `[DynamicDependency]` rooting to prevent `TypeLoader` from finding zero operations. |
| [CoreWCF #1750](https://github.com/CoreWCF/CoreWCF/issues/1750) | [CoPilot]: Migrate internal buffer pooling from `BufferManager` to `IBufferWriter<byte>` | **Open** | `g7ed6e` · Created 2026-07-19, Updated 2026-08-07 | — (references `CoreWCF/CoreWCF` `main` `InternalBufferManager.cs`) | Proposes 4-step migration away from `BufferManager` without breaking public `MessageEncoder` signatures: (1) internal `IBufferWriter<byte>` adapter over `BufferManager`, (2) migrate internal `HttpInput` / `MessageEncoder` call sites, (3) add public `IBufferWriter<byte>` overloads on `MessageEncoder`, (4) swap backing store to `ArrayPool<byte>.Shared`. Explicitly documents why swapping the pool before changing ownership breaks `MaxBufferPoolSize` and triggers `ArgumentException` in `PooledBufferManager.ReturnBuffer` (caught only by `#1751` failure). |
| [CoreWCF #1758](https://github.com/CoreWCF/CoreWCF/pull/1758) | Add an internal `IBufferWriter<byte>` adapter over `BufferManager` | **Open (Ready for Review)**, unmerged | `g7ed6e` · Created 2026-08-04, Updated 2026-08-07 | Head (`g7ed6e:refactor-buffer-writer`): `fd2f029fda0f9c27a3341f83dd6595dd3831292c` <br> Base (`CoreWCF:main`): `6e2cce88349fe41056fe8afda0dbbdf98d54bb00` | 1 commit, 6 files (`+1302/-74`). Implements steps 1–2 of `#1750` (superseding `#1751`): `BufferManagerBufferWriter` + `BufferedMessageStreamHelper` for `HttpInput.BufferMessageStreamAsync`, keeping the caller's `BufferManager` as the backing pool and fixing buffer leaks on quota/read exceptions. Introduces `RecordingBufferManager` double/foreign-return ownership tests. Leaves `BufferedOutputStream`/`BufferManagerOutputStream` untouched because `XmlDictionaryWriter` requires a `Stream` and `BufferedOutputStream.Dispose()` does not return buffers. Also notes live `FIXME`s in `Issue1382Test.cs` and `HttpClientTests.cs` where chunked `Transfer-Encoding` breaks message parsing in the ASP.NET Core pre-read path. |
| [CoreWCF #801](https://github.com/CoreWCF/CoreWCF/pull/801) | Introduce `MessageEncoder.ReadMessageAsync(ReadOnlySequence<byte> buffer)` | **Open (Draft)**, unmerged | `g7ed6e` · Created earlier, Updated 2026-08-08 | Head (`g7ed6e:refactor/use-pipereader-to-read-messages`): `cf6a04a1ddd6e401d9f0d516bc7dd3876068412b` <br> Base (`CoreWCF:main`): `6e2cce88349fe41056fe8afda0dbbdf98d54bb00` | 14 commits, 45 changed files (`+1800/-552`). Proposes `ReadOnlySequence<byte>` / `PipeReader` decoding on `MessageEncoder` instead of contiguous `ArraySegment<byte>`. Still draft and unmerged after >3 years, illustrating the complexity of changing buffer/lifetime contracts across CoreWCF's public channel/encoder layers. |
| [CoreWCF #1447](https://github.com/CoreWCF/CoreWCF/discussions/1447) | CoreWCF v1.6.0 release discussion | **Answered / Released** | CoreWCF maintainers (`v1.6.0` era; behavior verified in `v1.9.2`) | Verified in `v1.9.2` (`62a222a59084af52a94afccac37d842b930ea46b`) | Announced opt-in `<EnableCoreWCFOperationInvokerGenerator>true</EnableCoreWCFOperationInvokerGenerator>` (explicitly noting it is not sufficient for Native AOT due to remaining `Reflection.Emit`/reflection code). In `CoreWCF.Primitives` `1.9.2`, `build/CoreWCF.Primitives.targets` enables `EnableCoreWCFOperationInvokerGenerator` **by default** on `.NETCoreApp >= 7.0`, emitting `[assembly: CoreWCF.EnableCoreWCFOperationInvokerGenerator(true)]` and `file sealed class OperationInvokerN` via `CoreWCF.BuildTools`. `OperationInvokerBehavior` still falls back to reflection `TaskMethodInvoker`/`SyncMethodInvoker` if a key is missing, and generated invokers still allocate `new object[n]` (`AllocateInputs()`) and `ValueTask<(object returnValue, object[] outputs)>`. |
| [CoreWCF #1048](https://github.com/CoreWCF/CoreWCF/issues/1048) | CoreWCF 2.X breaking changes | **Open** | Updated 2025-07-10 | — | Tracks planned 2.x breaking changes: dropping `ServiceAuthorizationBehavior.AuthorizationService`, `DispatchRuntime.AuthorizationService`, synchronous security/auth overloads (`SecurityTokenAuthenticator`, `UserNamePasswordValidator`, `ServiceAuthenticationManager.Authenticate`, `ServiceAuthorizationManager`), and replacing `AppContext.TryGetSwitch` in `InvokerUtil.cs` with `RuntimeFeature.IsDynamicCodeSupported`. |

---

## 3. `dotnet/runtime` Upstream Issues

| Reference | Title | State | Dates | Key Findings & FastSoap Implications |
|---|---|---|---|---|
| [dotnet/runtime #125299](https://github.com/dotnet/runtime/issues/125299) | [API Proposal]: `System.Text.Xml` — modern XML serializer with span support, source generation, and familiar API surface | **Closed (`not_planned`)** | Created 2026-03-08, Closed 2026-03-09, Updated 2026-04-13 (6 comments) | **Critical status update:** Closed as `not_planned` by `@eiriktsarpalis` on 2026-03-09. The .NET runtime team confirmed they do **not** plan to introduce a parallel `System.Text.Xml` serialization stack in the BCL, citing scope comparable to `System.Text.Json` and lower ROI relative to `System.Xml`. Future BCL work, if any, would target incremental augmentations to `System.Xml` (`#106580`), leaving AOT/UTF-8 XML serialization to external/community packages. FastSoap **cannot** wait on a future BCL `System.Text.Xml` API. |
| [dotnet/runtime #106580](https://github.com/dotnet/runtime/issues/106580) | NativeAOT and `XmlSerializer` | **Open** | Created 2024-08-18, Updated 2026-08-25 (12 comments) | Documents `XmlSerializer` runtime failures (`TypeScope.GetDefaultIndexer`, `XmlReflectionImporter`, `[RequiresDynamicCode]`, `[RequiresUnreferencedCode]`) under `PublishAot`, including when using `dotnet-xscgen`-generated DTOs. Confirms BCL `XmlSerializer` is not viable for FastSoap's AOT runtime path. |
| [dotnet/runtime #71350](https://github.com/dotnet/runtime/issues/71350) | [NativeAOT] Data Contract Serializer | **Closed (`completed`)** | Created 2022-06-27, Closed 2022-06-28 (4 comments) | Older .NET 7 Native AOT bug report for `DataContractJsonSerializer` (`System.Drawing.Color`); does not make `DataContractSerializer` AOT-safe or warning-free (`RequiresDynamicCode` / `RequiresUnreferencedCode` remain on `DataContractSerializer` in .NET 10). |
| [dotnet/runtime #32555](https://github.com/dotnet/runtime/issues/32555) | Add async serialization support to `XmlSerializer` and `DataContractSerializer` | **Open** | Created 2020-02-19, Updated 2026-08-25 (21 comments) | Filed by `@mconnew` for WCF `IAsyncEnumerable` support. Both BCL serializers remain synchronous-only (`XmlReader`/`XmlWriter` based), forcing buffering or blocking if used directly in async pipelines. |

---

## 4. Exact Reproduction Commands

### A. Reproduce Released CoreWCF 1.9.2 Spike (`experiments/R0A-CoreWcfSpike/`)

```pwsh
# 1. Build and run HTTP smoke suite (baseline, custom formatter, custom encoder, negative action mismatch)
dotnet build experiments/R0A-CoreWcfSpike/R0A.CoreWcfSpike.csproj -c Release
dotnet run --project experiments/R0A-CoreWcfSpike/R0A.CoreWcfSpike.csproj -c Release --no-build

# 2. Inspect CoreWCF.BuildTools generated operation invokers
dotnet build experiments/R0A-CoreWcfSpike/R0A.CoreWcfSpike.csproj -c Release -p:EmitCompilerGeneratedFiles=true -p:CompilerGeneratedFilesOutputPath="$env:TEMP/cwcf_gen"

# 3. Run ILLink trim analysis
dotnet publish experiments/R0A-CoreWcfSpike/R0A.CoreWcfSpike.csproj -c Release -r win-x64 -p:PublishTrimmed=true -p:TrimmerSingleWarn=false -o "$env:TEMP/cwcf_trim"

# 4. Attempt Native AOT publish (requires VS Desktop Development with C++ workload for link.exe)
dotnet publish experiments/R0A-CoreWcfSpike/R0A.CoreWcfSpike.csproj -c Release -r win-x64 -p:PublishAot=true -o "$env:TEMP/cwcf_aot"

# 5. Run ILC AOT analysis without MSVC link.exe (reproduces all 343 ILC warnings up to link step)
dotnet publish experiments/R0A-CoreWcfSpike/R0A.CoreWcfSpike.csproj -c Release -r win-x64 -p:PublishAot=true -p:IlcUseEnvironmentalTools=true -p:TrimmerSingleWarn=false -o "$env:TEMP/cwcf_aot_ilc"
```

### B. Inspect / Reproduce Pinned Upstream CoreWCF Branches

```sh
# Clone CoreWCF at pinned main or v1.9.2 tag
git clone https://github.com/CoreWCF/CoreWCF.git
cd CoreWCF
git checkout aef18198b56b608d6940ddb2b8c0050a6bfe0862   # main snapshot (2026-11-19)
# or: git checkout 62a222a59084af52a94afccac37d842b930ea46b  # v1.9.2

# Fetch unmerged PR heads by exact commit SHA (from fork g7ed6e/CoreWCF)
git fetch https://github.com/g7ed6e/CoreWCF.git 878e002ea6854d43f9b5131272872b23d642e263  # PR #1766
git fetch https://github.com/g7ed6e/CoreWCF.git fd2f029fda0f9c27a3341f83dd6595dd3831292c  # PR #1758
git fetch https://github.com/g7ed6e/CoreWCF.git cf6a04a1ddd6e401d9f0d516bc7dd3876068412b  # PR #801
```
