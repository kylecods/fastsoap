# R0-A Research Report: CoreWCF Extension Spike, AOT Assessment & Test Suite Survey

**Date:** 2026-11-26  
**Work Package:** R0-A  
**Spike Location:** `experiments/R0A-CoreWcfSpike/`  
**Pinned Baseline:** `CoreWCF.Http` `1.9.2`, `CoreWCF.Primitives` `1.9.2` (commit `62a222a59084af52a94afccac37d842b930ea46b`, MIT)  
**Environment:** Windows 10.0.22631 (`win-x64`), .NET SDK `11.0.100`, Target `net10.0` (`Microsoft.AspNetCore.App` / `Microsoft.NETCore.App` `10.0.15`), C# 15 (`<LangVersion>15</LangVersion>`).

---

## 1. Executive Summary (Gate R0-A Evidence)

Extending released CoreWCF (`1.9.2`) through its public formatter (`IDispatchMessageFormatter`) and encoder (`MessageEncodingBindingElement` / `MessageEncoder`) seams **works cleanly under JIT**, and `CoreWCF.BuildTools` source-generates `IOperationInvoker` implementations by default on `.NETCoreApp >= 7.0`. However, the spike proves four structural blockers for using CoreWCF as FastSoap's AOT-first, `System.Xml`-free runtime:

1. **Unavoidable `System.Xml` & `Message` Coupling:** Even when replacing `DataContractSerializer` with a custom `IDispatchMessageFormatter` and wrapping `MessageEncoder`, CoreWCF's public dispatch/encoder contract mandates `CoreWCF.Channels.Message`, `BodyWriter`, `System.Xml.XmlDictionaryReader` (`System.Xml.XmlUTF8TextReader` at runtime), and `System.Xml.XmlDictionaryWriter` (`System.Xml.XmlDictionaryAsyncCheckWriter` at runtime). `CoreWCF.Primitives` also transitively references `System.Web.Services.Description`, `System.Security.Cryptography.Xml`, `Microsoft.IdentityModel.Xml`, `Microsoft.IdentityModel.Tokens.Saml`, `System.DirectoryServices`, and `System.Security.Cryptography.ProtectedData`.
2. **Severe Native AOT / Trim Warning Footprint:** Running `ILCompiler` (`10.0.15`) on the minimal Echo + Transfer spike yields **343 ILC warnings** (`209 IL3050`, `74 IL2026`, `11 IL2075`, `10 IL2070`, `6 IL2046`, `6 IL2072`, `6 IL3051`, `4 IL2077`, `4 IL3054`, `3 IL2067`, `2 IL2055`, `2 IL2071`, `2 IL2080`, `2 IL2090`, `1 IL2060`, `1 IL2091`), plus **127 ILLink warnings** under `PublishTrimmed`. Notably, `IL3054` aborts generic recursion in `CoreWCF.Dispatcher.AndMessageFilterTable<FilterDataPair<...>>`, which throws at runtime if reached.
3. **Generated Invokers Still Allocate `object[]` & Async State Machines:** `CoreWCF.BuildTools` generated `OperationInvoker0` and `OperationInvoker1` (verified active at runtime), yet `IOperationInvoker` still requires `AllocateInputs() => new object[1]` and `ValueTask<(object returnValue, object[] outputs)>` per call.
4. **Local Native Linker Prerequisite Blocker:** Standard `dotnet publish -c Release -r win-x64 -p:PublishAot=true` failed on this machine because the Visual Studio Desktop Development for C++ workload (`link.exe`) is not installed (`Microsoft.NETCore.Native.Windows.targets(142,5)`). Passing `-p:IlcUseEnvironmentalTools=true` ran ILC compilation to completion and produced the exact 343 warnings before failing at the final `link.exe` step (`MSB3073`).

---

## 2. Spike Implementation & Observed JIT Behavior

`experiments/R0A-CoreWcfSpike/` hosts two SOAP 1.1 contracts (`IEchoService` under `urn:echo`, and nested `IPaymentService.TransferAsync(TransferRequest)` under `urn:payments`) across three endpoint configurations on Kestrel:

| Endpoint Path | Binding / Seams | Active Invoker (Observed) | Active Formatter (Observed) |
|---|---|---|---|
| `/baseline/echo.svc` | `BasicHttpBinding` | Generated `OperationInvoker0` | `CoreWCF.Dispatcher.PrimitiveOperationFormatter` |
| `/baseline/payments.svc` | `BasicHttpBinding` | Generated `OperationInvoker1` | `CoreWCF.Dispatcher.DataContractSerializerOperationFormatter` |
| `/custom-formatter/echo.svc` | `BasicHttpBinding` + `CustomFormatterEndpointBehavior` | Generated `OperationInvoker0` | `R0A.CoreWcfSpike.EchoManualDispatchFormatter` |
| `/custom-formatter/payments.svc` | `BasicHttpBinding` + `CustomFormatterEndpointBehavior` | Generated `OperationInvoker1` | `R0A.CoreWcfSpike.TransferManualDispatchFormatter` |
| `/custom-encoder/echo.svc` | `CustomBinding(InstrumentedSoap11EncodingBindingElement, HttpTransportBindingElement)` + `CustomFormatterEndpointBehavior` | Generated `OperationInvoker0` | `R0A.CoreWcfSpike.EchoManualDispatchFormatter` |
| `/custom-encoder/payments.svc` | `CustomBinding(InstrumentedSoap11EncodingBindingElement, HttpTransportBindingElement)` + `CustomFormatterEndpointBehavior` | Generated `OperationInvoker1` | `R0A.CoreWcfSpike.TransferManualDispatchFormatter` |

### 2.1 Observed HTTP Smoke Output (`dotnet run -c Release`)

Command:
```pwsh
dotnet build experiments/R0A-CoreWcfSpike/R0A.CoreWcfSpike.csproj -c Release
dotnet run --project experiments/R0A-CoreWcfSpike/R0A.CoreWcfSpike.csproj -c Release --no-build
```

Observed output (abbreviated only to remove blank spacing; full wire payloads preserved verbatim):
```text
Listening: http://127.0.0.1:64038
Runtime: 10.0.15 | IsDynamicCodeSupported=True

=== Dispatch & Invoker Diagnostics ===
/baseline/echo.svc | Op=Echo | Action=urn:echo/IEchoService/Echo | Invoker=CoreWCF.Dispatcher.<OperationInvoker_g>F259B82CA263A17823C665F8B777CE597AA8AA4598EBFAE7B6D89BAE3BBE75F04__OperationInvoker0 | Formatter=CoreWCF.Dispatcher.PrimitiveOperationFormatter
/custom-formatter/echo.svc | Op=Echo | Action=urn:echo/IEchoService/Echo | Invoker=CoreWCF.Dispatcher.<OperationInvoker_g>F259B82CA263A17823C665F8B777CE597AA8AA4598EBFAE7B6D89BAE3BBE75F04__OperationInvoker0 | Formatter=R0A.CoreWcfSpike.EchoManualDispatchFormatter
/custom-encoder/echo.svc | Op=Echo | Action=urn:echo/IEchoService/Echo | Invoker=CoreWCF.Dispatcher.<OperationInvoker_g>F259B82CA263A17823C665F8B777CE597AA8AA4598EBFAE7B6D89BAE3BBE75F04__OperationInvoker0 | Formatter=R0A.CoreWcfSpike.EchoManualDispatchFormatter
/baseline/payments.svc | Op=Transfer | Action=urn:payments/IPaymentService/Transfer | Invoker=CoreWCF.Dispatcher.<OperationInvoker_g>F259B82CA263A17823C665F8B777CE597AA8AA4598EBFAE7B6D89BAE3BBE75F04__OperationInvoker1 | Formatter=CoreWCF.Dispatcher.DataContractSerializerOperationFormatter
/custom-formatter/payments.svc | Op=Transfer | Action=urn:payments/IPaymentService/Transfer | Invoker=CoreWCF.Dispatcher.<OperationInvoker_g>F259B82CA263A17823C665F8B777CE597AA8AA4598EBFAE7B6D89BAE3BBE75F04__OperationInvoker1 | Formatter=R0A.CoreWcfSpike.TransferManualDispatchFormatter
/custom-encoder/payments.svc | Op=Transfer | Action=urn:payments/IPaymentService/Transfer | Invoker=CoreWCF.Dispatcher.<OperationInvoker_g>F259B82CA263A17823C665F8B777CE597AA8AA4598EBFAE7B6D89BAE3BBE75F04__OperationInvoker1 | Formatter=R0A.CoreWcfSpike.TransferManualDispatchFormatter

[Baseline Echo] HTTP 200 OK | Content-Type=text/xml; charset=utf-8
<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><EchoResponse xmlns="urn:echo"><EchoResult>hello-fastsoap</EchoResult></EchoResponse></s:Body></s:Envelope>

[Baseline Transfer] HTTP 200 OK | Content-Type=text/xml; charset=utf-8
<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><TransferResponse xmlns="urn:payments"><TransferResult xmlns:i="http://www.w3.org/2001/XMLSchema-instance"><TransactionId>TX-123456</TransactionId><Successful>true</Successful></TransferResult></TransferResponse></s:Body></s:Envelope>

[CustomFormatter Echo] HTTP 200 OK | Content-Type=text/xml; charset=utf-8
<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><EchoResponse xmlns="urn:echo"><EchoResult>hello-fastsoap</EchoResult></EchoResponse></s:Body></s:Envelope>

[CustomFormatter Transfer] HTTP 200 OK | Content-Type=text/xml; charset=utf-8
<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><TransferResponse xmlns="urn:payments"><TransferResult><TransactionId>TX-123456</TransactionId><Successful>true</Successful></TransferResult></TransferResponse></s:Body></s:Envelope>

[CustomEncoder+Formatter Echo] HTTP 200 OK | Content-Type=text/xml; charset=utf-8
<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><EchoResponse xmlns="urn:echo"><EchoResult>hello-fastsoap</EchoResult></EchoResponse></s:Body></s:Envelope>

[CustomEncoder+Formatter Transfer] HTTP 200 OK | Content-Type=text/xml; charset=utf-8
<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><TransferResponse xmlns="urn:payments"><TransferResult><TransactionId>TX-123456</TransactionId><Successful>true</Successful></TransferResult></TransferResponse></s:Body></s:Envelope>

[Negative ActionMismatch] HTTP 500 InternalServerError | Content-Type=text/xml; charset=utf-8
<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><s:Fault><faultcode xmlns:a="http://schemas.microsoft.com/ws/2005/05/addressing/none">a:ActionNotSupported</faultcode><faultstring xml:lang="en-US">The message with Action 'urn:echo/IEchoService/UnknownAction' cannot be processed at the receiver, due to a ContractFilter mismatch at the EndpointDispatcher. This may be because of either a contract mismatch (mismatched Actions between sender and receiver) or a binding/security mismatch between the sender and the receiver.  Check that sender and receiver have the same contract and the same binding (including security requirements, e.g. Message, Transport, None).</faultstring></s:Fault></s:Body></s:Envelope>

=== Seam Observations ===
Formatter DeserializeCalls=4, SerializeCalls=4
Formatter LastReaderType=System.Xml.XmlUTF8TextReader
Formatter LastWriterType=System.Xml.XmlDictionaryAsyncCheckWriter
Encoder LastInnerEncoderType=CoreWCF.Channels.TextMessageEncoderFactory+TextMessageEncoder
Encoder ReadArraySegmentCalls=2, ReadStreamCalls=0
Encoder WriteBufferedCalls=2, WriteStreamCalls=0
Encoder BufferManagerTakeCount=2, LastReadBytes=264, LastWriteBytes=254, LastRentalCapacity=512
```

### 2.2 Generated Operation Invoker Verification (`CoreWCF.BuildTools`)

In `CoreWCF.Primitives` `1.9.2`, `build/CoreWCF.Primitives.targets` enables `EnableCoreWCFOperationInvokerGenerator` automatically whenever `TargetFrameworkIdentifier == '.NETCoreApp'` and `TargetFrameworkVersion >= 7.0`, emitting `[assembly: CoreWCF.EnableCoreWCFOperationInvokerGenerator(true)]`. Inspecting the emitted `OperationInvoker.g.cs` (via `-p:EmitCompilerGeneratedFiles=true`) confirms:

- `CoreWCF.BuildTools.OperationInvokerGenerator` emits `file sealed class OperationInvoker0` and `OperationInvoker1` implementing `CoreWCF.Dispatcher.IOperationInvoker`, registered via `[ModuleInitializer]` into `DispatchOperationRuntimeHelpers.RegisterOperationInvoker`.
- **Remaining dispatch allocations:** Even with generated invokers active, `AllocateInputs()` allocates `new object[1]` per request, `InvokeAsync(object instance, object[] inputs)` boxes/unboxes through `object[]`, and `InvokeAsync` returns `ValueTask<(object returnValue, object[] outputs)>`.
- **Note on `PrimitiveOperationFormatter`:** For `Echo(string)`, CoreWCF automatically selected `CoreWCF.Dispatcher.PrimitiveOperationFormatter` instead of `DataContractSerializerOperationFormatter`, confirming PR `#1766`'s observation that body serializer seams in `DataContractSerializerOperationFormatter` alone miss primitive operations.

---

## 3. Seam Findings: Copies, Transcoding, `System.Xml` Coupling & `BufferManager`

| Layer / Seam | Observed Behavior in CoreWCF `1.9.2` |
|---|---|
| **HTTP Request Intake (`HttpInput` + `MessageEncoder`)** | Under default buffered `BasicHttpBinding` / `CustomBinding`, `HttpInput.BufferMessageStreamAsync` buffers the incoming stream using `BufferManager` and invokes `MessageEncoder.ReadMessage(ArraySegment<byte>, BufferManager, string)` (`ReadArraySegmentCalls=2`, `ReadStreamCalls=0` on the two custom-encoder calls). |
| **Encoding & Transcoding (`TextMessageEncoder`)** | `TextMessageEncoder` passes the UTF-8 `ArraySegment<byte>` to `System.Xml.XmlUTF8TextReader` (no full-message UTF-16 `string` pre-conversion at the encoder boundary). However, reading element content via `XmlDictionaryReader.ReadElementContentAsString()` / `DataContractSerializer` materializes UTF-16 `string`s per XML element/member, and `Message` parses/tracks SOAP headers through `XmlDictionaryReader` and `MessageHeaders`. |
| **Response Writing (`BodyWriter` + `MessageEncoder.WriteMessage`)** | `BodyWriter.OnWriteBodyContents(XmlDictionaryWriter)` receives `System.Xml.XmlDictionaryAsyncCheckWriter` wrapping `XmlUTF8TextWriter` over `BufferManagerOutputStream`, then `TextMessageEncoder.WriteMessage` consolidates into a `BufferManager`-rented `byte[]` (`LastWriteBytes=254`, `LastRentalCapacity=512` via `TrackingBufferManager.TakeBuffer`). |
| **Formatter Seam (`IDispatchMessageFormatter`)** | Replacing `DispatchOperation.Formatter` cleanly bypasses `DataContractSerializer` for body DTOs (note: baseline `DataContractSerializer` emitted `xmlns:i="http://www.w3.org/2001/XMLSchema-instance"` on `<TransferResult>`, whereas our manual formatter did not unless explicitly written). Whitespace between elements requires explicit `reader.MoveToContent()` before `reader.ReadEndElement()` when using `XmlDictionaryReader`. |
| **Encoder Seam (`MessageEncoder`)** | CoreWCF's `MessageEncoder` has **no** synchronous `Stream` overloads (only `ReadMessageAsync(Stream, int, string)`, `ReadMessage(ArraySegment<byte>, BufferManager, string)`, `WriteMessageAsync(Message, Stream)`, `WriteMessage(Message, int, BufferManager, int)`), and `MessageEncodingBindingElement` is server-only (`BuildChannelFactory` removed). A custom encoder still cannot avoid `CoreWCF.Channels.Message` or `BufferManager` ownership without rewriting CoreWCF's entire channel/dispatcher pipeline. |
| **Assembly / `System.Xml` Coupling** | `CoreWCF.Primitives.dll` directly references `System.Web.Services.Description` (`8.1.2.0`), `System.Security.Cryptography.Xml` (`8.0.0.0`), `Microsoft.IdentityModel.Xml` (`8.19.1.0`), `Microsoft.IdentityModel.Tokens.Saml` (`8.19.1.0`), `System.DirectoryServices` (`4.0.0.0`), `System.DirectoryServices.Protocols` (`4.0.0.0`), and `System.Security.Cryptography.ProtectedData` (`8.0.0.0`), in addition to `netstandard` `System.Xml` and `System.Runtime.Serialization`. |

---

## 4. Trim & Native AOT Publish Attempts

### 4.1 Native AOT Publish (`-c Release -r win-x64 -p:PublishAot=true`)

Command:
```pwsh
dotnet publish experiments/R0A-CoreWcfSpike/R0A.CoreWcfSpike.csproj -c Release -r win-x64 -p:PublishAot=true -o "$env:TEMP/aot-pub"
```
Result: **Failed (`exit 1`) before ILC** due to missing Windows native C++ linker prerequisites:
```text
C:\Users\Kyle.rotich\.nuget\packages\microsoft.dotnet.ilcompiler\10.0.15\build\Microsoft.NETCore.Native.Windows.targets(142,5): error : Platform linker not found. Ensure you have all the required prerequisites documented at https://aka.ms/nativeaot-prerequisites, in particular the Desktop Development for C++ workload in Visual Studio. For ARM64 development also install C++ ARM64 build tools. [D:\MOVEMENT\DESKTOP\PERSONAL\FastSoap\experiments\R0A-CoreWcfSpike\R0A.CoreWcfSpike.csproj]
```

### 4.2 `PublishTrimmed` Analysis (`-c Release -r win-x64 -p:PublishTrimmed=true -p:TrimmerSingleWarn=false`)

Command:
```pwsh
dotnet publish experiments/R0A-CoreWcfSpike/R0A.CoreWcfSpike.csproj -c Release -r win-x64 -p:PublishTrimmed=true -p:SuppressTrimAnalysisWarnings=false -p:TrimmerSingleWarn=false -o "$env:TEMP/trim-pub"
```
Result: **Published (`exit 0`) with 127 ILLink warnings** (`TrimMode` defaults to `partial`, so non-trimmable `CoreWCF` assemblies were not stripped; running the published JIT binary passed all 6 endpoint checks + negative check with `exit 0`).

ILLink warning counts:
- `IL2026`: 74
- `IL2070`: 11
- `IL2075`: 11
- `IL2046`: 8
- `IL2072`: 6
- `IL2077`: 4
- `IL2067`: 3
- `IL2055`: 2
- `IL2071`: 2
- `IL2080`: 2
- `IL2090`: 2
- `IL2060`: 1
- `IL2091`: 1

### 4.3 Full ILC AOT Analysis (`-p:PublishAot=true -p:IlcUseEnvironmentalTools=true -p:TrimmerSingleWarn=false`)

Passing `-p:IlcUseEnvironmentalTools=true` bypasses the pre-ILC `findvcvarsall.bat` check in `Microsoft.NETCore.Native.Windows.targets`, running `ILCompiler` (`10.0.15`) completely through native object emission before failing on `link.exe` (`MSB3073`, exit code 9009).

Result: **343 ILC warnings** emitted across `CoreWCF` and rooted `System.Runtime.Serialization` paths:

| Warning Code | Count | Primary Root Causes Observed |
|---|---:|---|
| `IL3050` | 209 | `RequiresDynamicCode`: `InvokerUtil.CriticalHelper.GenerateCreateInstanceDelegate` (`MakeGenericMethod`), `XmlSerializerOperationBehavior` (`XmlReflectionImporter`), `DataContractSerializer` (`PrimitiveDataContract` / `NullPrimitiveDataContract`), `XsdDataContractExporterWrapper`, `LambdaExpressionBuilder` |
| `IL2026` | 74 | `RequiresUnreferencedCode`: `HttpTransportBindingElement.GetProperty<T>` (`Assembly.GetType`), `DataContractSerializerOperationFormatter`, `XmlSerializerOperationFormatter`, `FaultFormatter`, `MessageFault.GetDetail<T>`, `XmlObjectSerializerHeader`, `SctClaimSerializer`, `SessionSecurityTokenHandler`, `CryptoHelper`, `NTAuthenticationNet8` |
| `IL2075` | 11 | Unannotated reflection in `TypeLoader<T>.GetKnownTypes` and `DataContractEx` |
| `IL2070` | 10 | Unannotated `Type` reflection in `InvokerUtil.HasDefaultConstructor`, `CustomAttributeProvider` |
| `IL2046` | 6 | `RequiresUnreferencedCode` mismatch on `XmlSerializerObjectSerializer` overrides |
| `IL2072` | 6 | `DispatcherBuilder.BuildAuthorizeData`, `XmlSerializerOperationFormatter.DeserializeBody`, `CryptoHelper` |
| `IL3051` | 6 | `RequiresDynamicCode` mismatch on `XmlSerializerObjectSerializer` overrides |
| `IL2077` | 4 | `TypedHeaderManager.GetTypedHeaderManager`, `NTAuthenticationNet8` |
| `IL3054` | 4 | **Generic recursion aborted** in `CoreWCF.Dispatcher.AndMessageFilterTable<FilterDataPair<...>>` (throws at runtime if reached) |
| `IL2067` | 3 | `TypeLoader<T>.GetIOperationAttributesFromType<TOp>`, `OperationFormatter.CreateTypedMessageInstance` |
| `IL2055` | 2 | `DataContractSerializerOperationFormatter.GetSubstituteDataContract` |
| `IL2071` | 2 | `InvokerUtil.CriticalHelper.GenerateCreateInstanceDelegate` (`CreateInstanceOfClass<T>` / `CreateInstanceOfStruct<T>`) |
| `IL2080` | 2 | `XmlDataContractEx.GetXmlTypeInfo` |
| `IL2090` | 2 | `ServiceReflector.GetInterfaces<TService>` |
| `IL2060` | 1 | `Security.NegotiateInternal.LambdaExpressionBuilder` |
| `IL2091` | 1 | `ServiceChannelProxy.CreateProxy<TChannel>` |
| **Total** | **343** | **All 343 warnings come from CoreWCF and BCL types it roots (0 from spike code)** |

Breakdown by namespace/type prefix:
- `CoreWCF.Dispatcher`: 79
- `System.Runtime.Serialization.*` (rooted via `PrimitiveOperationFormatter`, `FaultContractInfo`, `MessageFault`, `MessageHeader`, `SctClaimSerializer`, etc.): 123 (`40 + 40 + 36 + 4 + 3`)
- `CoreWCF.Security` + `CoreWCF.IdentityModel`: 69 (`57 + 12`)
- `CoreWCF.Description`: 27
- `CoreWCF.Runtime`: 17
- `CoreWCF.Channels`: 16
- Other (`TypedHeaderManager`, `ServiceAuthorizationManager`, `System.Linq.Expressions`, `DispatchProxyGenerator`, `ComInterop`): 12

**Key architectural conclusion:** Even with `EnableCoreWCFOperationInvokerGenerator=true` and custom `IDispatchMessageFormatter`s on all user operations, CoreWCF `1.9.2` unconditionally roots `DataContractSerializer`, `XmlSerializer`, `SctClaimSerializer`, `FaultFormatter`, `TypeLoader`, and `AndMessageFilterTable`. Achieving a warning-free Native AOT binary on top of CoreWCF is not achievable via user-level extensions alone.

---

## 5. CoreWCF Test Suite Survey (Compatibility & Accuracy Catalog)

Surveyed against `CoreWCF/CoreWCF` `main` (`aef18198b56b608d6940ddb2b8c0050a6bfe0862`; 739 test/BuildTools files total, including 263 files in `src/CoreWCF.Http/tests` and 83 files in `src/CoreWCF.Primitives/tests`).

### 5.1 Suites FastSoap Must Validate Against (In-Scope Wire & Protocol Profiles)

| Category | CoreWCF Test Suite / File | Specific Cases & Semantics to Validate | FastSoap Target Profile |
|---|---|---|---|
| **SOAP 1.1 / 1.2 `BasicHttpBinding` & Versions** | `CoreWCF.Http/tests/BasicScenariosTest.cs`<br>`CoreWCF.Http/tests/BasicValidationSoapTests.cs`<br>`CoreWCF.Http/tests/MessageVersionsTests.cs`<br>`CoreWCF.Http/tests/BasicHttpBindingPropertyTests.cs`<br>`CoreWCF.Http/tests/SimpleTest.cs`<br>`CoreWCF.Http/tests/RequestReplyTests.cs` | Request/reply over HTTP POST, `Soap11` (`text/xml`, `SOAPAction`) and `Soap12` (`application/soap+xml`, `action` parameter), `BasicHttpSecurityMode.None`, `Action`/`ReplyAction` overrides, `SimpleTest` baseline Echo | M1 (SOAP 1.1), M2 (SOAP 1.2 without WS-Addressing). *Exclude `Soap11WSAddressing10`/`Soap12WSAddressing*` from v0.1.* |
| **Action Routing & Mismatch** | `CoreWCF.Http/tests/OpActionReplyActionBehaviorTests.cs`<br>`CoreWCF.Http/tests/OpContractInvalidActionReplyActionTests.cs`<br>`CoreWCF.Http/tests/ActionMismatchTests.cs` | `Action` / `ReplyAction` routing, untyped/custom actions, invalid/ambiguous action rejection (HTTP 500 `a:ActionNotSupported` in SOAP 1.1 without invoking service method) | M1/M2 (HTTP `SOAPAction` vs body QName mismatch; WS-Addressing mismatch excluded in v0.1) |
| **`DataContractSerializer` Wire Shapes** | `CoreWCF.Http/tests/ComplexSerializationTests.cs`<br>`CoreWCF.Http/tests/MCWrappedMultiNSTests.cs`<br>`CoreWCF.Http/tests/ServiceWithCoreWCFMessageParameterTest.cs`<br>`CoreWCF.Http/tests/ServiceContractNameTests.cs`<br>`CoreWCF.Http/tests/ServiceContractNameSpaceTests.cs`<br>`CoreWCF.Http/tests/ServiceWithContractInheritanceTests.cs`<br>`CoreWCF.Http/tests/TaskPrimitivesTest.cs`<br>`CoreWCF.Http/tests/TaskCollectionsTests.cs`<br>`CoreWCF.Http/tests/InterestingTypeTests.cs`<br>`CoreWCF.Primitives/tests/SerializationTests.cs`<br>`CoreWCF.Primitives/tests/ServiceKnownTypeTests.cs` | Wrapped parameter naming (`[MessageParameter]`, `[ServiceContract(Name, Namespace)]`), multi-namespace contracts (`MCWrappedMultiNSTests`), inheritance (`TypesWithDCInheritance`), `Task<T>` primitives & collections, `Order`/`IsRequired`/`EmitDefaultValue`, `i:nil`, enums, dates, `decimal`, plus PR `#1766`'s 86-case oracle corpus (`GoldenRecordTests` / `ReadObjectTests`) | M1/M2 explicit wire DTOs & oracle comparison (explicitly exclude/reject unsupported DCS features such as `[Serializable]` private fields, `ISerializable`, `IXmlSerializable`, runtime `[KnownType("Method")]`) |
| **`XmlSerializer` Format** | `CoreWCF.Http/tests/ServiceWithCoreWCFXmlSerializerFormatTest.cs`<br>`CoreWCF.Http/tests/ServiceWithCoreWCFXmlSerializerFormatOnOperationTest.cs`<br>`CoreWCF.Http/tests/ServiceWithSSMXmlSerializerFormatTest.cs`<br>`CoreWCF.Primitives/tests/Soap/ServiceModelSoapTests.cs` | `Document`/`Literal` `XmlSerializerFormat` (`Identity` / `EchoLiteral` / `ComplexMessage_OperationFormatUseLiteral`), attribute vs element ordering, `xsi`/`xsd` root/body namespace declarations | M2 explicit wire mapping fixtures. *Explicitly exclude `OperationFormatUse.Encoded` (`EchoEncoded` / `ComplexMessage_OperationFormatUseEncoded`).* |
| **SOAP Faults** | `CoreWCF.Http/tests/FaultContractTests.cs`<br>`CoreWCF.Http/tests/FaultContractNameTests.cs`<br>`CoreWCF.Http/tests/ServiceWithCoreWCFFaultContractTest.cs`<br>`CoreWCF.Http/tests/ServiceWithCoreWCFFaultContractWithNamespaceAtServiceContractLevelTest.cs`<br>`CoreWCF.Http/tests/OperationFaultContractInfoAwareServiceTests.cs`<br>`CoreWCF.Primitives/tests/BasicFaultTest.cs`<br>`CoreWCF.Primitives/tests/ErrorHandling/FaultContracts.cs` | SOAP 1.1 `<s:Fault>` (`faultcode`, `faultstring`, `detail`) vs SOAP 1.2 `<s:Fault>` (`Code`, `Reason` with `xml:lang`, `Detail`), typed `[FaultContract]` detail serialization, custom fault names/namespaces, `FaultException` parity | M1 (basic fault mapping), M2 (SOAP 1.2 fault mapping & registered typed fault details) |
| **SOAP Headers** | `CoreWCF.Http/tests/ServiceWithMessageBodyAndHeaderTest.cs`<br>`CoreWCF.Primitives/tests/MessageEncoderTest.cs` (`ObjectHeaderTests`)<br>`CoreWCF.Primitives/tests/MessageTests.cs`<br>`CoreWCF.Primitives/tests/Contracts/EchoMessageContract.cs` | Custom SOAP header extraction (`MessageHeader`), arrays/complex types in headers, `mustUnderstand` and role/actor targeting, preserving namespace context | M2 (`mustUnderstand`, roles, registered header handlers) |
| **Message Framing, Quotas & Buffers** | `CoreWCF.Http/tests/MessageEncodingTests.cs`<br>`CoreWCF.Http/tests/LargeRequestTests.cs`<br>`CoreWCF.Http/tests/RequestDelegateHandlerMaxBodySizeTests.cs`<br>`CoreWCF.Http/tests/RegressionTests/Issue1382Test.cs`<br>`CoreWCF.Primitives/tests/MessageEncoderTest.cs`<br>`CoreWCF.Primitives/tests/VerifyBufferPools.cs`<br>`CoreWCF.Primitives/tests/VerifyBufferUsed.cs` | `byte[]` base64 encoding, large payloads (1 KiB / 1 MiB / 10 MiB), Kestrel `MaxRequestBodySize` enforcement, chunked `Transfer-Encoding` intake (where CoreWCF had `FIXME`s in `Issue1382Test.cs`), invalid XML characters (`InvalidCharToString`), `BufferManager` pool sizing & deterministic return tests (plus PR `#1758` `RecordingBufferManager` parity tests) | M1/M2 (`SoapRequestBuffer` quotas, chunked/multi-segment `BodyReader` intake, deterministic pool accounting) |
| **Authorization & DI Hosting** | `CoreWCF.Http/tests/Authorization/*` (23 files)<br>`CoreWCF.Http/tests/DependencyInjection/*` | ASP.NET Core endpoint/operation authorization (`[Authorize]`, policy failure -> 401/403 before service execution), scoped/keyed DI lifetimes | M1 (endpoint auth), M2 (operation policy) |

### 5.2 CoreWCF Suites Explicitly Out of Scope for FastSoap v0.1

- `CoreWCF.NetTcp/tests/*` (61 files), `CoreWCF.NetNamedPipe/tests/*` (14 files), `CoreWCF.MSMQ/tests/*` (12 files), `CoreWCF.Kafka/tests/*` (36 files), `CoreWCF.RabbitMQ/tests/*` (17 files), `CoreWCF.UnixDomainSocket/tests/*` (21 files), `CoreWCF.WebHttp/tests/*` (40 files)
- WS-Security / SAML / Negotiate (`Saml*.cs`, `NTAuthenticationNet8EncryptTests.cs`, `WSHttpBindingTests.cs`, `SimpleWSFedBinding.cs`), MTOM (`MtomMessageEncodingBindingElementTests.cs`), Duplex/WebSockets (`WebSocketsTests.cs`, `DuplexTestService.cs`), RPC/Encoded (`OperationFormatUse.Encoded`), and `ConfigurationManager` XML config (`CoreWCF.ConfigurationManager/tests/*`).
