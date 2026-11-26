# Dependency Licenses & Provenance

**Last verified:** 2026-11-26  
**Scope:** All packages centrally pinned in [Directory.Packages.props](../../Directory.Packages.props) and platform toolchains (A0 Foundation)

## 1. Runtime & Generator Dependencies

| Package | Pinned Version | Upstream Source / Commit SHA | License | Allowed Scope | `System.Xml*` Dependency | Notes |
|---|---|---|---|---|---|---|
| `XmlEssentials` | `1.0.0` | `https://github.com/CloudNimble/XmlEssentials.git` (`44b8a0d90bd62c7f83e94f2b4c63f87defbd4134`, tag `v1.0.0`, published `2026-11-25T20:23:00Z`) | MIT (`<license type="expression">MIT</license>`) | `FastSoap.Core` (and samples/tests; **not** `FastSoap.Abstractions`) | None (verified via `MetadataReader` on `lib/net10.0/CloudNimble.XmlEssentials.dll`) | Zero NuGet dependencies; bundles `analyzers/dotnet/cs/CloudNimble.XmlEssentials.SourceGeneration.dll`. Upgrade freeze: do not move to `v1.0.1` without a re-qualification ADR. |
| `Microsoft.CodeAnalysis.CSharp` | `4.14.0` | `https://github.com/dotnet/roslyn` (`8edf7bcd4f1594c3d68a6a567469f41dbd33dd1b`, branch `release/dev17.14`) | MIT (`<license type="expression">MIT</license>`) | `FastSoap.Generators` (build-time `netstandard2.0` analyzer only; never shipped as runtime dependency) | N/A (compile-time analyzer) | Compatible with `.NET 10`/`11` SDKs, Visual Studio `17.14+`, and `BenchmarkDotNet` `0.15.8`. |

### Package Contents Verified in `XmlEssentials.1.0.0.nupkg`

- `XmlEssentials.nuspec`: declares `<license type="expression">MIT</license>`, `<authors>CloudNimble, Inc.</authors>`, `<repository type="git" url="https://github.com/CloudNimble/XmlEssentials.git" branch="refs/heads/main" commit="44b8a0d90bd62c7f83e94f2b4c63f87defbd4134" />`, and empty dependency groups for `net10.0` and `net11.0`.
- `LICENSE`: MIT License (`Copyright (c) 2026 CloudNimble`), permissive for commercial and open-source redistribution with copyright and permission notice preservation.
- `lib/net10.0/CloudNimble.XmlEssentials.dll` (397,312 bytes) and `lib/net11.0/CloudNimble.XmlEssentials.dll` (398,848 bytes).
- `analyzers/dotnet/cs/CloudNimble.XmlEssentials.SourceGeneration.dll` (157,696 bytes, `netstandard2.0` Roslyn incremental generator bundled inside the main package).
- `build/XmlEssentials.props` and `buildTransitive/XmlEssentials.props`.

### Note on Upstream `XmlEssentials` `v1.0.1`

GitHub tag `v1.0.1` (`f7bd3a3376db16b5d4151d0ac2fae26da001544e`, `2026-11-26T18:06:53Z`) exists upstream with breaking metadata API changes (`XmlPropertyInfoValues<TDeclaring, TValue>`, `XmlTypeInfo<T>.CreateObject`, `Order` sorting semantics). Under `ADR-002` and `ADR-011`, `XmlEssentials` remains pinned at `1.0.0` until a dedicated re-qualification ADR approves any upgrade.

## 2. Test, Oracle, and Benchmark Dependencies (Non-Runtime Only)

These packages are strictly confined to `tests/*` and `benchmarks/*`. They MUST NOT be referenced directly or transitively by `FastSoap.Abstractions`, `FastSoap.Core`, `FastSoap.AspNetCore`, or `FastSoap.Metadata`.

| Package | Pinned Version | Upstream Source / Commit SHA | License | Allowed Scope | Notes |
|---|---|---|---|---|---|
| `xunit.v3` | `3.2.2` | `https://github.com/xunit/xunit` (`728c1dce012cd82193035dddfeaba184baaa88c6`) | Apache-2.0 (`<license type="expression">Apache-2.0</license>`) | `tests/*` | xUnit v3 test framework (`xunit.v3.mtp-v1` `3.2.2`). |
| `xunit.runner.visualstudio` | `4.0.1` | `https://github.com/xunit/visualstudio.xunit` (`0f7d528729972578553fb146cbde5f95b316cdc4`) | Apache-2.0 (`<license type="expression">Apache-2.0</license>`) | `tests/*` (`PrivateAssets=all`) | Supports xUnit v1, v2, and v3 (`net8.0+`) in VSTest / `dotnet test`. |
| `Microsoft.NET.Test.Sdk` | `18.12.0` | `https://github.com/microsoft/vstest` (`8c03f23cd7df6ab28c3f1e3d112dea978eae8b0c`) | MIT (`<license type="expression">MIT</license>`) | `tests/*` | MSBuild test host targets and coverage integration. |
| `CoreWCF.Http` | `1.9.2` | `https://github.com/CoreWCF/CoreWCF` (`62a222a59084af52a94afccac37d842b930ea46b`) | MIT (`<license type="expression">MIT</license>`) | Oracle tests & benchmarks only | Upgraded from `1.8.0` (which carried 7 NuGet CVEs) to `1.9.2`. |
| `CoreWCF.Primitives` | `1.9.2` | `https://github.com/CoreWCF/CoreWCF` (`62a222a59084af52a94afccac37d842b930ea46b`) | MIT (`<license type="expression">MIT</license>`) | Oracle tests & benchmarks only | Primary compatibility oracle & JIT baseline (with generated invokers). |
| `SoapCore` | `1.2.1.18` | `https://github.com/DigDes/SoapCore` (`4eb088b3eccc27a72b6040e8d43b7ab4317ae612`) | MIT (`<license type="expression">MIT</license>`) | Benchmarks only | Secondary JIT baseline. |
| `System.ServiceModel.Http` | `10.0.652802` | `https://github.com/dotnet/wcf` (`e9d8c1c2a051618689bc22ab263f6ff0f2493d64`) | MIT (`<license type="expression">MIT</license>`) | Compatibility / interop / oracle tests only | Real WCF client (`ChannelFactory<T>`) for wire interop testing. |
| `System.ServiceModel.Primitives` | `10.0.652802` | `https://github.com/dotnet/wcf` (`e9d8c1c2a051618689bc22ab263f6ff0f2493d64`) | MIT (`<license type="expression">MIT</license>`) | Compatibility / interop / oracle tests only | WCF client contracts and bindings. |
| `BenchmarkDotNet` | `0.15.8` | `https://github.com/dotnet/BenchmarkDotNet` (`1e8238ef6c08e18821edae83ac8d6b0a2bb1bb98`) | MIT (`<license type="expression">MIT</license>`) | `benchmarks/*` only | Microbenchmark harness (`Microsoft.CodeAnalysis.CSharp 4.14.0` dependency matches generator pin). |

## 3. Platform & Toolchain Dependencies

| Component | Version / Source | License | Notes |
|---|---|---|---|
| .NET SDK (`global.json`) | `11.0.100` (`297acf930f`) | MIT | Compiles `net10.0` projects with `<LangVersion>15</LangVersion>`. |
| `Microsoft.NETCore.App` / `Microsoft.AspNetCore.App` | `10.0.15` targeting/runtime packs | MIT | `.NET 10` LTS runtime baseline for FastSoap runtime, tests, and baselines (`FrameworkReference` in `FastSoap.AspNetCore`). |
| `Microsoft.DotNet.ILCompiler` | `10.0.15` (NuGet, restored on `PublishAot=true`) | MIT | Build/publish-time Native AOT compiler (`ilc.exe`). Not redistributed at runtime. |

## 4. Compliance Summary

- Zero GPL, AGPL, LGPL, or other copyleft dependencies across all runtime, generator, test, oracle, and benchmark packages.
- Only permissive **MIT** and **Apache-2.0** licensed packages are pinned in `Directory.Packages.props`.
- Zero `System.Xml*` runtime references in `CloudNimble.XmlEssentials.dll` and in FastSoap runtime libraries (`FastSoap.Abstractions`, `FastSoap.Core`, `FastSoap.AspNetCore`), enforced by automated `PEReader`/`MetadataReader` unit tests.
- No third-party mocking frameworks are permitted anywhere in the repository (`AGENTS.md` §5).
