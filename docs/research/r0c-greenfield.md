# R0-C Greenfield Independent Runtime Spike Results

**Date:** 2026-11-26  
**Project:** [R0C-GreenfieldSpike.csproj](file:///D:/MOVEMENT/DESKTOP/PERSONAL/FastSoap/experiments/R0C-GreenfieldSpike/R0C-GreenfieldSpike.csproj)  
**Environment:** Windows 10.0.22631 (x64), .NET SDK `11.0.100`, target framework `net10.0`, `<LangVersion>15</LangVersion>`, `XmlEssentials` `1.0.0`, `Microsoft.DotNet.ILCompiler` `10.0.15`.

## 1. Scope Implemented

The isolated spike in `experiments/R0C-GreenfieldSpike/` implements the independent-runtime data path from `FastSoap-Implementation-Plan-v2.md`:

- **Bounded Request Intake ([SoapRequestBuffer.cs](file:///D:/MOVEMENT/DESKTOP/PERSONAL/FastSoap/experiments/R0C-GreenfieldSpike/Buffers/SoapRequestBuffer.cs)):** Incremental `PipeReader` consumption into a single `ArrayPool<byte>.Shared` rental growing geometrically up to `MaxRequestBytes` (1 MiB), advancing the pipe in `finally`, returning replaced/abandoned buffers with optional clearing (`clearOnReturn: true`), and rejecting oversized `Content-Length` early with HTTP `413`.
- **Bounded UTF-8 Envelope Reader ([SoapEnvelopeReader.cs](file:///D:/MOVEMENT/DESKTOP/PERSONAL/FastSoap/experiments/R0C-GreenfieldSpike/Protocol/SoapEnvelopeReader.cs)):** Synchronous `Utf8XmlReader` pass enforcing `MaxDepth = 64`, `MaxAttributeCount = 64`, `MaxNameByteCount = 1024`, `MaxDocumentByteCount = 1 MiB`, `MaxTextByteCount = 1 MiB`, XML 1.0 only, PI rejection, DTD rejection, SOAP 1.1 envelope/header/body grammar, `mustUnderstand` check, ordinal `FrozenDictionary<string, ISoapOperation>` routing, exact Body child QName check, and `XmlSerializer.Deserialize(ref reader, typeInfo)` inside the envelope's live namespace scope.
- **Authored XmlEssentials Context & Wire DTOs ([PaymentContracts.cs](file:///D:/MOVEMENT/DESKTOP/PERSONAL/FastSoap/experiments/R0C-GreenfieldSpike/Contracts/PaymentContracts.cs)):** `[XmlSerializable<T>]` context (`SpikeXmlContext`) for `EchoRequest`, `EchoResponse`, `TransferRequestWire` (`urn:payments` with nested `TransferRequest`), and `TransferResponseWire` (`urn:payments` with nested `TransferResult`). `XmlSerializer.IsReflectionEnabledByDefault` is disabled (`false`, trimmed).
- **Staged SOAP 1.1 Reply & Fault Writer ([BoundedPooledBufferWriter.cs](file:///D:/MOVEMENT/DESKTOP/PERSONAL/FastSoap/experiments/R0C-GreenfieldSpike/Buffers/BoundedPooledBufferWriter.cs), [SoapResponseWriter.cs](file:///D:/MOVEMENT/DESKTOP/PERSONAL/FastSoap/experiments/R0C-GreenfieldSpike/Protocol/SoapResponseWriter.cs)):** Stages full responses (2 MiB cap) and faults (16 KiB cap) via `Utf8XmlWriter` before setting `StatusCode`, `ContentType`, and `ContentLength` and copying to `HttpResponse.BodyWriter`.
- **Kestrel Host ([Program.cs](file:///D:/MOVEMENT/DESKTOP/PERSONAL/FastSoap/experiments/R0C-GreenfieldSpike/Program.cs)):** `WebApplication.CreateSlimBuilder`, plain `RequestDelegate` (`MapPost("/Service.svc", ...)`), and atomic invocation counters (`s_echoInvocations`, `s_transferInvocations`) exposed on `GET /health`.

## 2. Build, Trim/AOT Analysis, and Native AOT Status

### Commands Executed & Observed Outcomes

1. **Full Native AOT Publish (`dotnet publish -c Release -p:PublishAot=true`):**
   ```powershell
   dotnet publish D:\MOVEMENT\DESKTOP\PERSONAL\FastSoap\experiments\R0C-GreenfieldSpike\R0C-GreenfieldSpike.csproj -c Release -p:PublishAot=true
   ```
   - Managed compilation (`R0C-GreenfieldSpike -> ...\FastSoap.R0C.GreenfieldSpike.dll`): **succeeded with 0 warnings** (`TreatWarningsAsErrors=true`, `IsAotCompatible=true`).
   - Native link step: **blocked** at `Microsoft.NETCore.Native.Windows.targets(142,5)`:
     `error : Platform linker not found. Ensure you have all the required prerequisites documented at https://aka.ms/nativeaot-prerequisites, in particular the Desktop Development for C++ workload in Visual Studio.`
   - Verified on host: Visual Studio Community 2026 (`18.12.0`) has `VC\Auxiliary` and `VC\Redist` only (no `VC\Tools\MSVC` / `link.exe`), and `C:\Program Files (x86)\Windows Kits\10` has only `UnionMetadata` (no `Lib` / `Include`).

2. **ILCompiler (`ilc.exe`) AOT Codegen Verification:**
   ```powershell
   dotnet publish D:\MOVEMENT\DESKTOP\PERSONAL\FastSoap\experiments\R0C-GreenfieldSpike\R0C-GreenfieldSpike.csproj -c Release -p:PublishAot=true -p:IlcUseEnvironmentalTools=true -r win-x64
   ```
   - `Generating native code` (`ilc.exe`) completed with **0 trim/AOT warnings** and produced `obj\Release\net10.0\win-x64\native\FastSoap.R0C.GreenfieldSpike.obj` (`56,973,102` bytes).
   - Failed only at `LinkNative` (`error MSB3073: The command ""link" @"obj\Release\net10.0\win-x64\native\link.rsp"" exited with code 9009`) due to missing `link.exe`.

3. **Self-Contained Trimmed Publish (`dotnet publish -c Release -p:PublishAot=false -p:PublishTrimmed=true -r win-x64 -p:SelfContained=true`):**
   - Succeeded with **0 trim/AOT warnings**.
   - Publish output: `25,305,390` bytes total (`FastSoap.R0C.GreenfieldSpike.exe`: `162,816` bytes), **0 `System.Xml*.dll` files** in output.
   - Note: Launching the `PublishTrimmed=true` + `PublishAot=false` build failed at Kestrel startup with `TypeLoadException: Method 'GetEnumerator' in type 'System.Security.Cryptography.X509Certificates.X509Certificate2Collection' ... does not have an implementation` inside `KestrelConfigurationLoader.Reload()`, which is a known ASP.NET Core 10 restriction (`PublishAot=true` is the supported AOT/trimming configuration for Kestrel, not `PublishTrimmed=true` without `PublishAot`).

4. **Release Reflection-Disabled Kestrel Build (`dotnet publish -c Release -p:PublishAot=false -p:PublishTrimmed=false`):**
   - Succeeded with **0 warnings**.
   - Binary sizes: `FastSoap.R0C.GreenfieldSpike.dll` = `54,784` bytes (`54,272` bytes under `win-x64`), `FastSoap.R0C.GreenfieldSpike.exe` = `162,816` bytes, `CloudNimble.XmlEssentials.dll` = `397,312` bytes.
   - Verified via `PEReader`/`MetadataReader`: `FastSoap.R0C.GreenfieldSpike.dll` has **zero `System.Xml*` assembly references**, and `CloudNimble.XmlEssentials.dll` has **zero `System.Xml*` assembly references**.

## 3. Observed Startup & Live HTTP POST Results

*(Measured on the Release reflection-disabled Kestrel executable `FastSoap.R0C.GreenfieldSpike.exe --urls http://127.0.0.1:5198` with `XmlSerializer.IsReflectionEnabledByDefault=false`; **not** a linked Native AOT binary due to the host linker blocker above.)*

- **Host Startup (`R0C_READY`):** `1451.03 ms` (`Stopwatch` from `Main` entry to `app.StartAsync()` completion)
- **Process Launch to First HTTP 200 Echo Response:** `2798.25 ms` (includes process spawn, Kestrel bind, `HttpClient` initialization, and first request)

### HTTP Test Matrix

| Test Case | Request Summary | Observed HTTP Status & Headers | Observed Response Body | Service Invocations |
|---|---|---|---|---|
| Valid `Echo` | `SOAPAction: "urn:echo/IEchoService/Echo"`, `<Echo xmlns="urn:echo"><Message>Hello FastSoap</Message></Echo>` | `200`, `text/xml; charset=utf-8`, `Content-Length: 179` | `<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><EchoResponse xmlns="urn:echo"><EchoResult>Hello FastSoap</EchoResult></EchoResponse></s:Body></s:Envelope>` | `echo = 1` |
| Valid `Transfer` (`urn:payments`, default `xmlns`) | `SOAPAction: "urn:payments/IPaymentService/Transfer"`, `<Transfer xmlns="urn:payments"><request><AccountNumber>123456</AccountNumber><Amount>100.00</Amount></request></Transfer>` | `200`, `text/xml; charset=utf-8`, `Content-Length: 251` | `<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><TransferResponse xmlns="urn:payments"><TransferResult><TransactionId>TX-001</TransactionId><Successful>true</Successful></TransferResult></TransferResponse></s:Body></s:Envelope>` | `transfer = 1` |
| Valid `Transfer` (prefixed `p:`) | `SOAPAction: "urn:payments/IPaymentService/Transfer"`, `xmlns:p="urn:payments"` on `<s:Envelope>`, `<p:Transfer><p:request><p:AccountNumber>999000</p:AccountNumber><p:Amount>42.50</p:Amount></p:request></p:Transfer>` | `200`, `text/xml; charset=utf-8`, `Content-Length: 251` | `<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><TransferResponse xmlns="urn:payments"><TransferResult><TransactionId>TX-002</TransactionId><Successful>true</Successful></TransferResult></TransferResponse></s:Body></s:Envelope>` | `transfer = 2` |
| Invalid XML (mismatched closing tag inside payload) | `<Echo xmlns="urn:echo"><Message>hi</Broken></Echo>` | `400`, `text/plain; charset=utf-8` | `Malformed XML in SOAP request.` | `0` |
| Invalid XML (DTD `<!DOCTYPE>`) | `<!DOCTYPE foo><s:Envelope ...>` | `400`, `text/plain; charset=utf-8`, `Content-Length: 30` | `Malformed XML in SOAP request.` | `0` |
| XML 1.1 rejection | `<?xml version="1.1" encoding="utf-8"?><s:Envelope ...>` | `400`, `text/plain; charset=utf-8` | `Only XML 1.0 is supported.` | `0` |
| Invalid UTF-8 bytes | Raw `0xFF 0xFE` inside `<Message>` | `400`, `text/plain; charset=utf-8` | `Malformed XML in SOAP request.` | `0` |
| Duplicate DTO element | `<Echo xmlns="urn:echo"><Message>a</Message><Message>b</Message></Echo>` | `400`, `text/plain; charset=utf-8` | `Malformed XML in SOAP request.` | `0` |
| Action mismatch | `SOAPAction: "urn:echo/IEchoService/Echo"` with `<Transfer xmlns="urn:payments">` body | `500`, `text/xml; charset=utf-8`, `Content-Length: 250` | `<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><s:Fault><faultcode>s:Client</faultcode><faultstring>SOAPAction 'urn:echo/IEchoService/Echo' requires body element '{urn:echo}Echo'.</faultstring></s:Fault></s:Body></s:Envelope>` | `0` |
| Missing body namespace | `SOAPAction: "urn:echo/IEchoService/Echo"` with `<Echo><Message>a</Message></Echo>` | `500`, `text/xml; charset=utf-8` | `<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><s:Fault><faultcode>s:Client</faultcode><faultstring>SOAPAction 'urn:echo/IEchoService/Echo' requires body element '{urn:echo}Echo'.</faultstring></s:Fault></s:Body></s:Envelope>` | `0` |

## 4. Blockers & Next Steps

- **Blocker (Host Environment):** Final Native AOT binary linking (`dotnet publish -c Release -p:PublishAot=true`) cannot complete on this Windows host because the Visual Studio "Desktop development with C++" workload (`link.exe` + Windows SDK libraries) is not installed. Managed AOT/trim analyzers and `ilc.exe` native object compilation (`56,973,102`-byte `.obj`) both succeeded with **0 warnings**.
- **Action Required for Full Native AOT Runtime Verification:** Install Visual Studio C++ Build Tools + Windows SDK on Windows (or publish `-r linux-x64` on a Linux host with `clang`) and re-run `dotnet publish -c Release -p:PublishAot=true` and the HTTP test script against the linked native executable to record true Native AOT binary size and startup latency.
