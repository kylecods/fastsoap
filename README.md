# FastSoap

An experimental, Native AOT-first, UTF-8-native SOAP 1.1 and SOAP 1.2 runtime for ASP.NET Core.

FastSoap reuses Kestrel, routing, dependency injection, authentication, authorization, TLS, and logging from ASP.NET Core, pairs them with [CloudNimble/XmlEssentials](https://github.com/CloudNimble/XmlEssentials) for source-generated UTF-8 XML serialization, and eliminates the legacy WCF channel stack, `Message` abstraction, and `System.Xml` DOM round-trips.

See [docs/architecture.md](docs/architecture.md) for the full architecture and request-processing walkthrough.

---

## Key Features

- **Zero `System.Xml*` in Core Runtime Libraries:** `FastSoap.Abstractions`, `FastSoap.Core`, `FastSoap.AspNetCore`, and `FastSoap.Metadata` carry zero `System.Xml*` assembly references.
- **AOT-First & Reflection-Free:** Built with `IsAotCompatible=true`, `EnableTrimAnalyzer=true`, `EnableAotAnalyzer=true`, `TrimmerSingleWarn=false`, and zero trim/AOT suppressions.
- **SOAP 1.1 & SOAP 1.2 over HTTP/HTTPS:** Strict document/literal wire profile, ordinal `SOAPAction` and `action` parameter matching, `FrozenDictionary` + `ReadOnlySpan<char>` alternate-lookup routing, header `mustUnderstand` / `actor` / `role` semantics, and version-specific sanitized SOAP Faults.
- **Bounded Memory & Security Quotas:** Injectable `ArrayPool<byte>` request/response buffers with deterministic `finally` returns, optional buffer clearing (`ClearPooledBuffers`), incremental `PipeReader` intake, XML 1.0 enforcement, DTD/XXE/PI/`encodingStyle` rejection, and configurable byte/depth/attribute/header quotas.
- **Manual & Source-Generated Registration:** Full parity between explicit `MapSoap` / `MapOperation` registration and Roslyn incremental source generation (`FastSoap.Generators`, diagnostics `FS0001`–`FS0008`).
- **Contract-First & Subset WSDL 1.1 / XSD Metadata:** Static and generated WSDL/XSD serving (`?wsdl`, `?xsd=`) with SHA-256 ETags and strict configured-address policy (`SoapMetadataAddressPolicy`).
- **Built-In Observability:** `ActivitySource("FastSoap")`, `Meter("FastSoap")`, and `[LoggerMessage]` structured logging with zero payload logging.

---

## Quick Start

### 1. Define Service Contract, Explicit Wire DTOs & `XmlSerializerContext`

```csharp
using CloudNimble.Text.Xml;
using CloudNimble.Text.Xml.Serialization;
using FastSoap.Abstractions;

[SoapService(Namespace = "urn:payments")]
public interface IPaymentService
{
    [SoapOperation(
        Name = "Transfer",
        Action = "urn:payments/IPaymentService/Transfer",
        ReplyAction = "urn:payments/IPaymentService/TransferResponse")]
    [SoapWireContract(
        RequestWireType = typeof(TransferRequestWire),
        ResponseWireType = typeof(TransferResponseWire),
        SerializerContextType = typeof(PaymentXmlContext),
        RequestElementName = "Transfer",
        RequestElementNamespace = "urn:payments",
        ResponseElementName = "TransferResponse",
        ResponseElementNamespace = "urn:payments")]
    Task<TransferResult> TransferAsync(TransferRequest request, CancellationToken cancellationToken);
}

[XmlRoot(ElementName = "Transfer", Namespace = "urn:payments")]
public sealed class TransferRequestWire
{
    [XmlElement(ElementName = "request", Namespace = "urn:payments")]
    public required TransferRequest Request { get; init; }
}

[XmlRoot(ElementName = "TransferResponse", Namespace = "urn:payments")]
public sealed class TransferResponseWire
{
    [XmlElement(ElementName = "TransferResult", Namespace = "urn:payments")]
    public required TransferResult Result { get; init; }
}

[XmlSerializable<TransferRequestWire>]
[XmlSerializable<TransferResponseWire>]
public partial class PaymentXmlContext : XmlSerializerContext
{
}
```

### 2. Host in ASP.NET Core

```csharp
using FastSoap.Abstractions;
using FastSoap.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args);
builder.Services.AddFastSoap(static options =>
{
    options.EnabledVersions = SoapVersionFlags.Both;
});
builder.Services.AddScoped<IPaymentService, PaymentService>();

WebApplication app = builder.Build();

// Source-generated registration (emitted by FastSoap.Generators)
app.MapPaymentService("/PaymentService.svc");

app.Run();
```

For manual registration, metadata configuration (`MapStaticMetadata` / `MapGeneratedMetadata`), and Native AOT hosting, see [samples/FastSoap.Sample.Payments/Program.cs](samples/FastSoap.Sample.Payments/Program.cs) and [samples/FastSoap.Sample.NativeAot/Program.cs](samples/FastSoap.Sample.NativeAot/Program.cs).

---

## Repository Layout

```text
FastSoap.slnx
Directory.Build.props
Directory.Packages.props
global.json
docs/
  architecture.md               # Architecture & request-processing walkthrough
src/
  FastSoap.Abstractions/        # Public contracts, descriptors, options, faults, attributes
  FastSoap.Core/                # UTF-8 protocol core, buffers, XmlEssentials adapter, routing
  FastSoap.AspNetCore/          # ASP.NET Core endpoint hosting, auth, metadata, telemetry
  FastSoap.Metadata/            # Static & subset WSDL 1.1 / XSD metadata provider & generator
  FastSoap.Generators/          # netstandard2.0 Roslyn incremental generator & diagnostics
samples/
  FastSoap.Sample.Payments/     # Manual + source-generated SOAP 1.1/1.2 sample host
  FastSoap.Sample.NativeAot/    # Native AOT sample host
tests/
  FastSoap.UnitTests/           # Abstractions & PEReader zero-System.Xml* verification
  FastSoap.SerializationTests/  # Buffer ownership (TrackingArrayPool) & XmlEssentials tests
  FastSoap.ProtocolTests/       # SOAP 1.1/1.2 grammar, headers, routing, fault matrix
  FastSoap.MetadataTests/       # Offline WSDL/XSD validation, ETag, address-injection tests
  FastSoap.GeneratorTests/      # Diagnostics FS0001-FS0008, incremental caching, two-generator tests
  FastSoap.IntegrationTests/    # Real Kestrel HTTP, auth, quota, abort & parity tests
  FastSoap.CompatibilityTests/  # Real WCF System.ServiceModel.Http client & CoreWCF 1.9.2 corpus
benchmarks/
  FastSoap.Benchmarks/          # BenchmarkDotNet & Kestrel HTTP load harness vs CoreWCF & SoapCore
```

---

## Building & Testing

Requires the .NET SDK pinned in `global.json` (`11.0.100`, targeting `net10.0` with C# 15).

```sh
dotnet restore FastSoap.slnx
dotnet build FastSoap.slnx -c Release --no-restore
dotnet test FastSoap.slnx -c Release --no-build -m:1
```

### Running Benchmarks

```sh
dotnet build benchmarks/FastSoap.Benchmarks/FastSoap.Benchmarks.csproj -c Release
dotnet run --project benchmarks/FastSoap.Benchmarks/FastSoap.Benchmarks.csproj -c Release --no-build -- --verify-allocs
dotnet run --project benchmarks/FastSoap.Benchmarks/FastSoap.Benchmarks.csproj -c Release --no-build -- --http-load
```

---

## Current Scope & Limitations (v0.1 Experimental)

- Targets SOAP 1.1 and SOAP 1.2 document/literal over HTTP/HTTPS with UTF-8 encoding.
- Not a drop-in CoreWCF replacement: WS-Addressing, WS-Security, MTOM, NetTcp, duplex, transactions, RPC/encoded, UTF-16, XML 1.1, and DTDs are excluded from v0.1.
- Outbound response `<s:Header>` emission and typed business fault `<detail>` payloads from service exceptions are planned follow-ups.
- Publishing a linked Native AOT binary (`dotnet publish samples/FastSoap.Sample.NativeAot -c Release -p:PublishAot=true`) requires the platform native C++ linker (MSVC C++ Build Tools + Windows SDK on Windows, or `clang` + `zlib1g-dev` on Linux).

---

## License

Licensed under the [MIT License](LICENSE).
