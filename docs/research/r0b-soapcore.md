# R0-B Research Report: SoapCore Adaptation Spike

**Work package:** R0-B (SoapCore adaptation spike)  
**Date:** 2026-11-26  
**Environment:** Windows 10 (OS Version 10.0.22631, win-x64), .NET SDK `11.0.100` (commit `297acf930f`), MSBuild `18.12.2-1.26529.118+297acf930f`, `Microsoft.AspNetCore.App` `11.0.0`  
**Spike location:** [experiments/R0B-SoapCoreSpike/](../../experiments/R0B-SoapCoreSpike/)

---

## 1. Pinned Upstream & Package Metadata

| Artifact | Pinned Value | Source / Verification |
|---|---|---|
| NuGet package | `SoapCore` `1.2.1.18` (published `2026-10-19T15:20:32Z`, listed) | `https://api.nuget.org/v3/registration5-semver1/soapcore/index.json` |
| Upstream repository | `https://github.com/DigDes/SoapCore` | `soapcore.nuspec` `<repository>` |
| Pinned tag & commit SHA | `refs/tags/v1.2.1.18` / `4eb088b3eccc27a72b6040e8d43b7ab4317ae612` (`develop` HEAD; note `master` is stale at `a6474c9e89787477cf5a321e9ccac0a62642b18d` from `2022-11-09`) | GitHub API + `soapcore.nuspec` |
| License | `MIT` | `LICENSE` at `4eb088b3eccc27a72b6040e8d43b7ab4317ae612`, `.nuspec` `<license type="expression">MIT</license>` |
| NuGet TFMs | `net8.0`, `netcoreapp3.1`, `netstandard2.0`, `netstandard2.1` | `soapcore.nuspec` |
| Dependencies (`net8.0`) | `System.CodeDom` `9.0.2`, `System.ServiceModel.Http` `8.1.1`, `Microsoft.AspNetCore.App` | `soapcore.nuspec` |
| Companion XML package | `XmlEssentials` `1.0.0` (`CloudNimble`, commit `44b8a0d90bd62c7f83e94f2b4c63f87defbd4134`, `MIT`, TFMs `net10.0`/`net11.0`, 0 NuGet dependencies) | `xmlessentials.nuspec` |

---

## 2. SoapCore Extension Seam Audit (`v1.2.1.18`)

All findings below are verified directly against `DigDes/SoapCore` commit `4eb088b3eccc27a72b6040e8d43b7ab4317ae612`.

### 2.1 Middleware & Transport (`SoapEndpointMiddleware.cs`, `SoapMessageEncoder.cs`, `ParsedMessage.cs`)

- **Hardcoded encoder & DOM buffering:** `SoapEndpointMiddleware<T_MESSAGE>` constructs `new SoapMessageEncoder(...)` directly in its constructor (not resolved via DI). `ProcessOperation` calls `ReadMessageAsync` -> `SoapMessageEncoder.ReadMessageAsync` -> `ParsedMessage.FromStreamAsync`, which copies `HttpRequest.Body` through a `System.IO.Pipelines.Pipe` + `StreamReader` into `XDocument.LoadAsync(...)`, extracts headers into `MessageHeaders`, and wraps the detached `<Body>` in a `ParsedMessage` (`System.ServiceModel.Channels.Message`).
- **Private WCF state mutation via reflection:** `ParsedMessage.ResetMessageConsumed()` calls `typeof(Message).GetProperty("State").SetValue(this, MessageState.Created)` so `GetReaderAtBodyContents()` / `WriteBodyContents()` can be re-read.
- **Unenforced encoder quotas on read:** `SoapMessageEncoder.ReadMessageAsync(Stream stream, int maxSizeOfHeaders, string contentType, CancellationToken ct)` accepts `maxSizeOfHeaders` and stores `ReaderQuotas`, **but never passes either to `ParsedMessage.FromStreamAsync`**.
- **Response staging via `System.Xml.XmlWriter`:** `SoapMessageEncoder.WriteMessageAsync` creates a `new MemoryStream()`, wraps it in `System.Xml.XmlWriter.Create(...)`, calls `message.WriteMessage(xmlTextWriter)`, sets `ContentLength`, and copies to `PipeWriter` (`httpContext.Response.BodyWriter`).
- **Pre-parse middleware seams (`ISoapMessageProcessor`, `IAsyncMessageFilter`, `IMessageInspector2`):** All three run **after** `ReadMessageAsync` (`ParsedMessage` / `XDocument`) has already executed, and all three operate on `System.ServiceModel.Channels.Message`.

### 2.2 Serialization Seam (`IXmlSerializationHandler`, `SerializerHelper.cs`, `ServiceBodyWriter.cs`)

- **Ingress-only `IXmlSerializationHandler`:** `SoapCoreOptions.UseCustomSerializer<TCustomSerializer>()` registers a resolver for `IXmlSerializationHandler`, which exposes only one method:
  ```csharp
  object DeserializeInputParameter(
      XmlDictionaryReader xmlReader,
      Type parameterType,
      string parameterName,
      string parameterNs,
      ICustomAttributeProvider customAttributeProvider,
      IEnumerable<Type> knownTypes = null);
  ```
- **Mandatory `XmlDictionaryReader` & runtime `Type` bridge:** Because `SoapEndpointMiddleware.GetRequestArguments` reads `<Operation>` via `XDocumentXmlReader` (`XmlDictionaryReader`) and passes `parameterType` at runtime, an `XmlEssentials`-backed `IXmlSerializationHandler` cannot read the HTTP UTF-8 stream/span directly. It must call `xmlReader.ReadOuterXml()` -> allocate a `string` -> encode UTF-8 `byte[]` -> construct `Utf8XmlReader` -> manually dispatch on `Type` to `XmlTypeInfo<T>`.
- **No response serialization seam:** `SoapEndpointMiddleware.CreateResponseMessage` hardcodes `internal class ServiceBodyWriter`, which switches exclusively on `SoapSerializer.XmlSerializer` (`CachedXmlSerializer` -> `System.Xml.Serialization.XmlSerializer`) or `SoapSerializer.DataContractSerializer` (`CachedDataContractSerializer` -> `System.Runtime.Serialization.DataContractSerializer`). In RPC mode it also uses `SoapReflectionImporter` and reflects over internal `Accessor`/`Name` properties. `out`/`ref` complex parameters are serialized via `DataContractSerializer`/`XmlSerializer` into `MemoryStream`, loaded into `XmlDocument`, and emitted via `writer.WriteRaw(...)`.
- **`System.CodeDom` & `MakeGenericMethod` in default serializer:** `SerializerHelper.DeserializeArrayXmlSerializer` instantiates `new CSharpCodeProvider()` (`System.CodeDom`) and calls `typeof(XmlSerializerExtensions).GetGenericMethod(...).MakeGenericMethod(elementType)` via reflection.
- **Attribute mismatch with `XmlEssentials`:** `SoapCore` (`OperationDescription`, `ServiceBodyWriter`, `MetaBodyWriter`) inspects `System.Xml.Serialization.*`, `System.Runtime.Serialization.*`, and `System.ServiceModel.*` attributes, whereas `XmlEssentials` `1.0.0` source generation (`XMLESS007` and namespace/element rules) binds its own `CloudNimble.Text.Xml.Serialization.*` attributes (`XmlRoot`, `XmlElement`, `XmlText`, etc.).

### 2.3 Service Discovery & Operation Dispatch (`ServiceDescription.cs`, `OperationDescription.cs`, `IOperationInvoker.cs`)

- **Unconditional assembly/type reflection at startup:** `SoapEndpointMiddleware` constructor runs `new ServiceDescription(options.ServiceType, ...)`, which scans `serviceType.Assembly.GetTypes()`, `GetInterfaces()`, `[ServiceContract]`, `[OperationContract]`, `DeclaredMethods`, `GetParameters()`, and `ReturnParameter`.
- **Replaceable invoker, fixed reflection envelope:** `IOperationInvoker.InvokeAsync(MethodInfo methodInfo, object instance, object[] inputs)` can be replaced in DI (removing `DefaultOperationInvoker`'s `MethodInfo.Invoke` and `Task<T>.Result` property reflection), **but** `SoapEndpointMiddleware` still supplies `MethodInfo` from `ServiceDescription` and boxes/packs parameters into `object[] arguments` per request.
- **Permissive action matching (`TryGetOperation`):** Matches `SOAPAction` with `StringComparison.OrdinalIgnoreCase` and falls back to substring matching (`methodName.IndexOf(HeadersHelper.GetTrimmedSoapAction(o.Name.AsSpan()), StringComparison.OrdinalIgnoreCase) >= 0`). Also reflects on `ServiceFilterAttribute` (`GetMethod("OnSoapActionExecuting")?.Invoke(...)`) and `ServiceType.GetProperty("MessageHeaders")?.SetValue(...)`.

### 2.4 WSDL / Metadata Seams (`MetaBodyWriter.cs`, `MetaWCFBodyWriter.cs`, `MetaFromFile.cs`, `IWsdlOperationNameGenerator.cs`)

- **Dynamic WSDL (`MetaBodyWriter` / `MetaWCFBodyWriter`):** Extends WCF `BodyWriter`, walks `ServiceDescription` and CLR object graphs via reflection (`GetProperties`, `GetFields`, `XmlReflectionImporter`, `XmlSchemas.Compile`, `HasShouldSerializeMethod`), and generates XSD/WSDL at runtime.
- **Static WSDL (`WsdlFileOptions` / `MetaFromFile`):** Reads local `.wsdl`/`.xsd` files into `System.Xml.XmlDocument` (`XmlResolver = null`), rewrites `<import>`/`<include>`/`<soap:address>` locations, and optionally transforms WSDL to HTML documentation via `SoapDefinition` (`System.Xml.Serialization.XmlSerializer`).
- **Host-header trust hazard:** `SoapEndpointMiddleware.GetServerUrl` reads `httpContext.Request.Headers["X-Forwarded-Host"]` unconditionally when `UrlOverride` and `UrlOverrideFunc` are unset.

---

## 3. Isolated Spike & Observed JIT Execution

Project: [experiments/R0B-SoapCoreSpike/R0B-SoapCoreSpike.csproj](../../experiments/R0B-SoapCoreSpike/R0B-SoapCoreSpike.csproj) (`net11.0`, C# 15, `SoapCore` `1.2.1.18`, `XmlEssentials` `1.0.0`).

Endpoints hosted in [Program.cs](../../experiments/R0B-SoapCoreSpike/Program.cs):
1. `/PaymentService.svc` — Baseline `SoapCore` (`SoapSerializer.XmlSerializer`) with `IPaymentService` (`Echo`, nested `Transfer` under `urn:payments`).
2. `/PaymentServiceXmlEssentials.svc` — `SoapCore` configured with `UseCustomSerializer<XmlEssentialsSerializationHandler>()` (bridging `XmlDictionaryReader` -> UTF-8 -> `Utf8XmlReader` + source-generated `PaymentXmlContext`) and `TypedPaymentOperationInvoker` (replacing `DefaultOperationInvoker`). Response writing still executes through `SoapCore`'s internal `ServiceBodyWriter` (`System.Xml.Serialization.XmlSerializer`) due to the missing response hook.

### Command & Output (JIT Release Smoke Test)

```powershell
dotnet build experiments/R0B-SoapCoreSpike/R0B-SoapCoreSpike.csproj -c Release --no-restore
dotnet run --project experiments/R0B-SoapCoreSpike/R0B-SoapCoreSpike.csproj -c Release --no-build -- --smoke-test
```

Observed result: `Build succeeded. 0 Warning(s), 0 Error(s)` (`7.42s`), exit code `0`:

```text
[Baseline Echo] HTTP 200 OK
<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"><s:Body><EchoResponse xmlns="urn:payments"><EchoResult>hello-fastsoap</EchoResult></EchoResponse></s:Body></s:Envelope>

[Baseline Transfer] HTTP 200 OK
<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"><s:Body><TransferResponse xmlns="urn:payments"><TransferResult><Successful>true</Successful><TransactionId>TX-123456</TransactionId></TransferResult></TransferResponse></s:Body></s:Envelope>

[XmlEssentials-Ingress Echo] HTTP 200 OK
<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"><s:Body><EchoResponse xmlns="urn:payments"><EchoResult>hello-fastsoap</EchoResult></EchoResponse></s:Body></s:Envelope>

[XmlEssentials-Ingress Transfer] HTTP 200 OK
<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"><s:Body><TransferResponse xmlns="urn:payments"><TransferResult><Successful>true</Successful><TransactionId>TX-123456</TransactionId></TransferResult></TransferResponse></s:Body></s:Envelope>

[Unknown Action Fault] HTTP 500 InternalServerError
<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"><s:Body><s:Fault><faultcode>s:Client</faultcode><faultstring>No operation found for specified action: urn:payments/IPaymentService/UnknownAction</faultstring></s:Fault></s:Body></s:Envelope>

[Invalid Payload Fault] HTTP 500 InternalServerError
<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"><s:Body><s:Fault><faultcode>s:Client</faultcode><faultstring>The value is not a valid decimal. Path: /request/Amount | LineNumber: 1 | BytePositionInLine: 98.</faultstring></s:Fault></s:Body></s:Envelope>

[WSDL] HTTP 200 OK (length=3775)
```

Note on fault leakage: `Invalid Payload Fault` proved `PaymentXmlContext` / `XmlEssentials` deserialized the `<request>` payload (`CloudNimble.Text.Xml.XmlException`), and also demonstrated that `SoapCore` returns HTTP 500 with raw exception details (`ex.Message`) in `<faultstring>` unless a custom `IFaultExceptionTransformer` or `ExceptionTransformer` is registered.

---

## 4. Native AOT & Trimming Verification

### 4.1 Native AOT Publish Attempt (`-c Release -p:PublishAot=true`)

Command:

```powershell
dotnet publish experiments/R0B-SoapCoreSpike/R0B-SoapCoreSpike.csproj -c Release -p:PublishAot=true
```

Observed result: **Failed (`exit code 1`)** during native toolchain check:

```text
C:\Users\Kyle.rotich\.nuget\packages\microsoft.dotnet.ilcompiler\11.0.0\build\Microsoft.NETCore.Native.Windows.targets(152,5): error : vswhere.exe failed to locate Visual Studio with Microsoft.VisualStudio.Component.VC.Tools.x86.x64. Ensure you have all the required prerequisites documented at https://aka.ms/nativeaot-prerequisites, in particular the Desktop Development for C++ workload in Visual Studio. For ARM64 development also install C++ ARM64 build tools.
```

Native AOT linking could not complete on this machine because the Visual Studio C++ build tools (`VC.Tools.x86.x64`) are not installed. Running `dotnet build experiments/R0B-SoapCoreSpike/R0B-SoapCoreSpike.csproj -c Release -p:PublishAot=true --no-restore` succeeded for the user project (`0 Warning(s)`) only because `SoapCore.dll` does not declare `IsAotCompatible` / `[RequiresDynamicCode]` / `[RequiresUnreferencedCode]` on `UseSoapEndpoint` / `AddSoapCore`, hiding its trim/AOT hazards from callers until `ILLink`/`ILC` analyzes `SoapCore.dll` itself.

### 4.2 Full Trimming & Analyzer Publish (`-p:PublishTrimmed=true -p:TrimMode=full`)

To capture the exact IL-level trim/AOT diagnostics inside `SoapCore` and its dependencies and test execution of the trimmed artifact:

```powershell
dotnet publish experiments/R0B-SoapCoreSpike/R0B-SoapCoreSpike.csproj -c Release -r win-x64 -p:PublishTrimmed=true -p:TrimMode=full -p:EnableAotAnalyzer=true -p:EnableTrimAnalyzer=true -p:TrimmerSingleWarn=false
```

Observed warning summary: **64 total trim/AOT warnings** (0 in `R0B-SoapCoreSpike`, 0 in `XmlEssentials`):

| Origin | Count | Warning Codes & Key Locations |
|---|---:|---|
| `SoapCore` (`1.2.1.18`) | 53 | `IL2026` (23), `IL2075` (19), `IL2070` (7), `IL2072` (2), `IL2060` (1), `IL2087` (1) |
| `System.ServiceModel.Primitives` (`8.1.1`) | 10 | `IL2026` (5), `IL2046` (5) |
| `System.CodeDom` (`9.0.2`) | 1 | `IL2057` (`CompilerInfo.CodeDomProviderType.get`) |

Representative `SoapCore` warnings directly observed in `ILLink` output:
- `IL2087`: `SoapEndpointExtensions.AddCustomSoapMessageSerializer<TProcessor>(IServiceCollection, ServiceLifetime)` (`SoapEndpointExtensions.cs(300,4)`) — missing `[DynamicallyAccessedMembers(PublicConstructors)]` on `TProcessor`.
- `IL2026`: `ServiceDescription.ServiceDescription(Type, Boolean)` (`ServiceDescription.cs(17,4)`), `CachedXmlSerializer` (`CachedXmlSerializer.cs(16,48)`), `SerializerHelper` (`SerializerHelper.cs(103,4)`, `(124,4)`), `ServiceBodyWriter` (`ServiceBodyWriter.cs(181,9)`, `(217,9)`, `(220,9)`, `(226,9)`, `(265,6)`, `(328,6)`, `(366,6)`), `FaultBodyWriter` (`FaultBodyWriter.cs(168,4)`), `MetaBodyWriter`/`BodyWriterExtensions` (`BodyWriterExtensions.cs(33,5)`, `(35,5)`, `(40,6)`, `(129,5)`), `SoapDefinition.DeserializeFromString/Stream` (`SoapMethods.cs`), `XmlSerializerExtensions.SerializeArray`, `XmlSerializerMessageHeader.OnWriteHeaderContents`.
- `IL2075`: `DefaultOperationInvoker.InvokeAsync` (`DefaultOperationInvoker.cs(21,5)`), `ServiceDescription`, `ContractDescription`, `OperationDescription.GetServiceKnownTypesHierarchy`, `SoapEndpointMiddleware.ExecuteFiltersAndTune` & `SetMessageHeadersToProperty`, `MetaBodyWriter`, `MetaWCFBodyWriter`, `FaultBodyWriter.ExtractFaultDetail`.
- `IL2072`: `SoapEndpointMiddleware.GetRequestArguments` (`(840,6)`) & `DeserializeParameters` (`(867,4)`) (`Activator.CreateInstance`).
- `IL2070` / `IL2060`: `ReflectionExtensions.GetGenericMethod` (`MakeGenericMethod`), `GetPropertyOrFieldMembers`, `MetaWCFBodyWriter`, `BodyWriterExtensions`.

### 4.3 Executing the Full-Trimmed Binary

Command:

```powershell
.\experiments\R0B-SoapCoreSpike\bin\Release\net11.0\win-x64\publish\R0B-SoapCoreSpike.exe --smoke-test
```

Observed result: **Crashed on startup (`exit code 1`)**:

```text
Unhandled exception. System.InvalidOperationException: A suitable constructor for type 'R0B.SoapCoreSpike.SoapCoreAdaptation.XmlEssentialsSerializationHandler' could not be located. Ensure the type is concrete and services are registered for all parameters of a public constructor.
   at Microsoft.Extensions.DependencyInjection.ServiceLookup.CallSiteFactory.CreateConstructorCallSite(...)
   at SoapCore.SoapEndpointExtensions.<>c__DisplayClass22_0`1.<AddSoapCore>b__1(Type serviceType) in /_/src/SoapCore/SoapEndpointExtensions.cs:line 215
   at SoapCore.SoapEndpointMiddleware`1..ctor(...) in /_/src/SoapCore/SoapEndpointMiddleware.cs:line 60
   ...
   at R0B.SoapCoreSpike.Program.Main(String[] args) in D:\MOVEMENT\DESKTOP\PERSONAL\FastSoap\experiments\R0B-SoapCoreSpike\Program.cs:line 64
```

This failure is directly caused by `SoapCore`'s `IL2087` defect in `AddCustomSoapMessageSerializer<TProcessor>()`, which trimmed away the public parameterless constructor of `XmlEssentialsSerializationHandler`.

---

## 5. Evaluation Against FastSoap Gate R0 Criteria

| Criterion | Finding on `SoapCore` `1.2.1.18` | Verdict |
|---|---|---|
| Avoid `Message` / `XmlReader` bridging | **Impossible via public seams.** `SoapMessageEncoder` creates `ParsedMessage` via `XDocument.LoadAsync` and `XDocumentXmlReader`; `IXmlSerializationHandler` only receives `XmlDictionaryReader` on ingress and does not exist on egress (`ServiceBodyWriter` is `internal` and hardcoded). | **Blocker** |
| Avoid runtime discovery & dynamic code | **Impossible without replacing core middleware.** `ServiceDescription` scans assemblies/types/attributes at startup via reflection; `UseSoapEndpoint` uses `ReflectionMiddlewareBinder`; `CachedXmlSerializer`, `SoapReflectionImporter`, `System.CodeDom`, `MakeGenericMethod`, and `Activator.CreateInstance` permeate serialization and WSDL generation. | **Blocker** |
| Native AOT / full trimming readiness | `53` trim warnings in `SoapCore` (+ `10` in `System.ServiceModel.Primitives`, `1` in `System.CodeDom`); full-trimmed binary crashes immediately on startup (`IL2087`). Native AOT linking failed on this Windows host due to missing VS C++ `VC.Tools.x86.x64` (`vswhere.exe`). | **Not AOT-safe** |
| Protocol reuse value | Low. `SoapMessageEncoder` ignores `maxSizeOfHeaders` and `ReaderQuotas`; `TryGetOperation` uses case-insensitive and substring matching; `GetServerUrl` trusts `X-Forwarded-Host`; default fault handling returns HTTP 500 with raw exception messages; response writing uses `MemoryStream` + `System.Xml.XmlWriter`. | **Not suitable for reuse** |
| Maintenance & adaptation cost | Adapting `SoapCore` to UTF-8-native `XmlEssentials` without `Message`/`XDocument`/`System.Xml` requires replacing `SoapEndpointMiddleware`, `SoapMessageEncoder`, `ParsedMessage`, `ServiceDescription`, `ServiceBodyWriter`, `DefaultFaultExceptionTransformer`, and `MetaBodyWriter`—leaving essentially zero reusable code while inheriting `System.ServiceModel.Http` and `System.CodeDom` dependencies. | **Prohibitive (full fork/rewrite)** |

**Recommendation for R0-D:** **Do not adapt or depend on `SoapCore`** for FastSoap's runtime. Retain `SoapCore` `1.2.1.18` solely as a JIT baseline/reference target in benchmarks and compatibility testing.
