# FastSoap Compatibility Corpus & Golden Wire Manifest

**Corpus Schema Version:** `1.0.0`  
**Work Package:** `R0-G` / `G1` (Compatibility corpus, golden wire fixtures, and CoreWCF 1.9.2 test-suite oracle)  
**Canonical Specification:** [FastSoap-Implementation-Plan-v2.md](../FastSoap-Implementation-Plan-v2.md) (§1.3, §3.2, §6, §11, §12.1)  
**Machine-Readable Index:** [tests/fixtures/manifest.json](../../tests/fixtures/manifest.json)  
**Oracle Harness:** [experiments/R0G-Oracle/FastSoap.R0G.Oracle.csproj](../../experiments/R0G-Oracle/FastSoap.R0G.Oracle.csproj)

---

## 1. Upstream Provenance, Versions & Licenses

All golden wire fixtures are captured **verbatim from live HTTP exchanges** between a real WCF client (`System.ServiceModel.Http`) or raw HTTP protocol probe and a real CoreWCF Kestrel server (`CoreWCF.Http` + `CoreWCF.Primitives`), plus standalone BCL `DataContractSerializer` and `XmlSerializer` payload runs under JIT on `.NET 10` (`net10.0`). No XML fixtures were hand-crafted for positive service contracts.

| Component | Version / Ref | Commit SHA / Assembly Version | License | Role in Corpus |
|---|---|---|---|---|
| `CoreWCF.Http` | `1.9.2` (tag `v1.9.2`) | `62a222a59084af52a94afccac37d842b930ea46b` (`1.9.0.0`) | MIT (.NET Foundation) | Live SOAP 1.1 / SOAP 1.2 Kestrel oracle server & WSDL metadata exporter (upgraded from `1.8.0`; zero `NU190x` warnings) |
| `CoreWCF.Primitives` | `1.9.2` (tag `v1.9.2`) | `62a222a59084af52a94afccac37d842b930ea46b` (`1.9.0.0`) | MIT (.NET Foundation) | CoreWCF dispatcher, message encoders, fault contracts, `ServiceMetadataBehavior` |
| `System.ServiceModel.Http` / `Primitives` | `10.0.652802` | `e9d8c1c2a051618689bc22ab263f6ff0f2493d64` (`10.0.0.0`) | MIT (.NET Foundation) | Live WCF `ChannelFactory<T>` client (`BasicHttpBinding` and `CustomBinding` SOAP 1.2) |
| CoreWCF PR `#1766` (reference only) | Open draft `feat/aot-datacontractserializer` | `878e002ea6854d43f9b5131272872b23d642e263` | MIT (.NET Foundation) | Taxonomy reference for `SanityTypes` corpus & immutable byte-exact fixture layout; **not referenced or imported** |
| .NET SDK / Runtime | `11.0.100` SDK / `10.0.15` (`net10.0`) | `297acf930f` | MIT | Unified `net10.0` (`LangVersion 15`) JIT oracle host & BCL `DataContractSerializer` / `XmlSerializer` |

---

## 2. Corpus Status Summary (No Denominator Manipulation)

All **63** oracle entries in [tests/fixtures/manifest.json](../../tests/fixtures/manifest.json) are verified live by `FastSoap.R0G.Oracle` on `net10.0` against `CoreWCF 1.9.2`, and all **63** entries are evaluated by `FastSoap.CompatibilityTests` (`GoldenCorpusTests`) with exact SHA-256 integrity checks (`0` unexpected failures, `0` untested):

- **FastSoap Observed Totals (63 entries):** **30 Passed** (`16` Positive Wire + `10` Serializer Fragments + `4` WSDL/XSD Metadata), **27 Excluded/Rejected** (with `0` service invocations), **6 Pending/Partial** (`4` Positive Wire + `2` Serializer Fragments), **0 Failed**, **0 Untested**.

| Category | Total Entries | Supported (v0.1 Target) | Excluded (Security / Out-of-Scope) | Pending (Post-M1 / Explicit Mapping) | Oracle Verified (`CoreWCF 1.9.2` / `net10.0`) | FastSoap Observed (`FastSoap.CompatibilityTests`) |
|---|---|---|---|---|---|---|
| Positive Wire (`soap11/`, `soap12/`) | 20 | 19 | 0 | 1 (`SOAP11-TRANSFER-DCS-DEFAULT`) | 20 / 20 | **16 Passed, 4 Pending/Partial** (`SOAP11-TRANSFER-DCS-DEFAULT`, `SOAP11-TRANSFER-FAULT-TYPED`, `SOAP12-TRANSFER-FAULT-TYPED`, `SOAP11-ECHO-HEADER`), **0 Failed, 0 Untested** |
| BCL Serializer Fragments (`serializers/`) | 12 | 12 | 0 | 0 | 12 / 12 | **10 Passed, 2 Pending/Partial** (`SER-DCS-TRANSFER-DEFAULT`, `SER-DCS-PAYMENT-FAULT`), **0 Failed, 0 Untested** |
| WSDL 1.1 / XSD Metadata (`wsdl/`) | 4 (11 files) | 4 | 0 | 0 | 4 / 4 | **4 Passed** (HTTP `?wsdl` / `?xsd=` byte-exact), **0 Failed, 0 Untested** |
| Negative / Adversarial (`negative/`) | 27 | 0 | 27 | 0 | 27 / 27 | **27 Excluded/Rejected** (`0` invocations; `NEG-18D` exercised via `MaxHeaderBytes = 65536`), **0 Failed, 0 Untested** |
| **Total Manifest Entries** | **63** | **35** | **27** | **1** | **63 / 63** | **30 Passed, 27 Excluded/Rejected, 6 Pending/Partial, 0 Failed, 0 Untested** |

---

## 3. Positive Wire Fixtures (`tests/fixtures/soap11/` and `tests/fixtures/soap12/`)

Every positive fixture includes `.request.xml`, `.response.xml`, and `.meta.json` (HTTP headers, status code, SHA-256 digests).

| Fixture ID | Version | Serializer | Wire Shape & Key Semantics | Expected Outcome | CoreWCF Test-Suite Mapping | Artifact Base |
|---|---|---|---|---|---|---|
| `SOAP11-ECHO-DCS` | SOAP 1.1 | `DataContractSerializer` | `EchoString("hello")` in `http://tempuri.org/`, `SOAPAction: "http://tempuri.org/IEchoService/EchoString"`, HTTP `200`, `text/xml; charset=utf-8` | `supported` | `CoreWCF.Http.Tests.SimpleTest.BasicHttpRequestReplyEchoString` | `soap11/echo/echo-dcs` |
| `SOAP11-ECHO-DCS-NONASCII` | SOAP 1.1 | `DataContractSerializer` | Multi-byte UTF-8 (`"Hello, 世界! € — café 🚀"`) roundtrip without BOM or transcoding loss | `supported` | `CoreWCF.Http.Tests.SimpleTest` + FastSoap Plan §12.1 (non-ASCII UTF-8) | `soap11/echo/echo-dcs-nonascii` |
| `SOAP11-ECHO-DCS-NULL` | SOAP 1.1 | `DataContractSerializer` | `EchoString(null)` emitting `xmlns:i="http://www.w3.org/2001/XMLSchema-instance"` and `i:nil="true"` | `supported` | `CoreWCF.Http.Tests.SimpleTest` + FastSoap Plan §12.1 (nil vs omission) | `soap11/echo/echo-dcs-null` |
| `SOAP11-ECHO-XMLSER` | SOAP 1.1 | `XmlSerializer` | `EchoString("hello")` with `[XmlSerializerFormat]` (`xmlns:xsi`/`xmlns:xsd` on `<s:Body>`) | `supported` | `CoreWCF.Http.Tests.ServiceWithCoreWCFXmlSerializerFormatTest` | `soap11/echo/echo-xmlser` |
| `SOAP11-ECHO-HEADER` | SOAP 1.1 | `XmlSerializer` | `[MessageContract]` with `<h:ApiKey>` request header, `<h:SayHi>` response header, and `<EchoHeaderRequest>`/`<EchoHeaderResponse>` body | `supported` | `CoreWCF.Http.Tests.ServiceWithMessageBodyAndHeaderTest.BasicScenarioServiceMessageBody` | `soap11/headers/echo-header-optional` |
| `SOAP11-TRANSFER-DCS-DEFAULT` | SOAP 1.1 | `DataContractSerializer` | `<Transfer xmlns="urn:payments"><request xmlns:a="http://schemas.datacontract.org/2004/07/...">` (CLR-derived data-contract namespace + alphabetical order + `i:nil="true"`) | `pending` | `CoreWCF.Http.Tests.ComplexSerializationTests.DataContractNameSpaceComplexSerialization` | `soap11/transfer/transfer-dcs-default` |
| `SOAP11-TRANSFER-DCS-URN` | SOAP 1.1 | `DataContractSerializer` | Nested `TransferRequest` (`AccountNumber`, `Amount` decimal `100.50`, `Currency`, nested `Metadata`, `Note`) and `TransferResult` (`ProcessedAtUtc` ISO-8601 UTC, `Status` enum `Completed`) in `urn:payments` | `supported` | `CoreWCF.Http.Tests.ServiceContractNameSpaceTests` + FastSoap Plan §3.1/§3.2 | `soap11/transfer/transfer-dcs-urnpayments` |
| `SOAP11-TRANSFER-DCS-NIL` | SOAP 1.1 | `DataContractSerializer` | Nested `TransferRequest` with `Metadata i:nil="true"` and `Note i:nil="true"` | `supported` | `CoreWCF.DataContractSerialization.TestCorpus.SanityNullable` + FastSoap Plan §12.1 | `soap11/transfer/transfer-dcs-urnpayments-nil` |
| `SOAP11-TRANSFER-XMLSER` | SOAP 1.1 | `XmlSerializer` | `[XmlSerializerFormat]` nested `Transfer` in `urn:payments` matching FastSoap Plan §3.2 wrapped wire DTO contract | `supported` | `CoreWCF.Http.Tests.ServiceWithCoreWCFXmlSerializerFormatTest` + FastSoap Plan §3.2 | `soap11/transfer/transfer-xmlser-urnpayments` |
| `SOAP11-BATCH-TRANSFER-DCS` | SOAP 1.1 | `DataContractSerializer` | `BatchTransfer` with `List<TransferRequest>` collection (populated + nil items) | `supported` | `CoreWCF.Http.Tests.TaskCollectionsTests` + PR `#1766` `SanityCollections` | `soap11/collections/batch-transfer-dcs` |
| `SOAP11-TRANSFER-FAULT-TYPED` | SOAP 1.1 | `DataContractSerializer` | HTTP `500`, `s:Client` fault with `<detail><PaymentFaultDetail xmlns="urn:payments">` | `supported` | `CoreWCF.Http.Tests.FaultContractTests.DatacontractFaults` | `soap11/faults/transfer-typed-fault` |
| `SOAP11-TRANSFER-FAULT-RECEIVER` | SOAP 1.1 | `DataContractSerializer` | Unhandled server exception (`IncludeExceptionDetailInFaults = false`), HTTP `500`, sanitized `InternalServiceFault` without exception/connection string leakage | `supported` | `CoreWCF.Primitives.Tests.BasicFaultTest` + FastSoap Plan §6.4 | `soap11/faults/transfer-receiver-fault` |
| `SOAP12-ECHO-DCS` | SOAP 1.2 | `DataContractSerializer` | `EchoString("hello")` on `http://www.w3.org/2003/05/soap-envelope` without WS-Addressing, `Content-Type: application/soap+xml; charset=utf-8; action="..."` | `supported` | `CoreWCF.Http.Tests.MessageVersionsTests.EchoStringTest` (`MessageVersion.Soap12`) | `soap12/echo/echo-dcs` |
| `SOAP12-ECHO-XMLSER` | SOAP 1.2 | `XmlSerializer` | `EchoString("hello")` over SOAP 1.2 HTTP binding without WS-Addressing | `supported` | `CoreWCF.Http.Tests.MessageVersionsTests` + `ServiceWithCoreWCFXmlSerializerFormatTest` | `soap12/echo/echo-xmlser` |
| `SOAP12-TRANSFER-DCS-URN` | SOAP 1.2 | `DataContractSerializer` | Nested `Transfer` in `urn:payments` over SOAP 1.2 HTTP binding without WS-Addressing | `supported` | `CoreWCF.Http.Tests.MessageVersionsTests` (`Soap12`) + FastSoap Plan §3.2 & §6.1 | `soap12/transfer/transfer-dcs-urnpayments` |
| `SOAP12-TRANSFER-XMLSER` | SOAP 1.2 | `XmlSerializer` | `[XmlSerializerFormat]` nested `Transfer` in `urn:payments` over SOAP 1.2 HTTP binding | `supported` | `CoreWCF.Http.Tests.MessageVersionsTests` (`Soap12`) + `ServiceWithCoreWCFXmlSerializerFormatTest` | `soap12/transfer/transfer-xmlser-urnpayments` |
| `SOAP12-TRANSFER-FAULT-TYPED` | SOAP 1.2 | `DataContractSerializer` | HTTP `500`, `<s:Fault><s:Code><s:Value>s:Sender</s:Value></s:Code><s:Reason><s:Text xml:lang="en-US">...</s:Text></s:Reason><s:Detail><PaymentFaultDetail ...>` | `supported` | `CoreWCF.Primitives.Tests.BasicFaultTest` + `FaultContractTests` | `soap12/faults/transfer-typed-fault` |
| `SOAP11-ECHO-DCS-UTF8BOM` | SOAP 1.1 | `DataContractSerializer` | Valid UTF-8 BOM (`EF BB BF`) prefix on valid `EchoString("hello")`; HTTP `200`, `EchoStringResponse` | `supported` | `CoreWCF` `TextMessageEncoder` UTF-8 BOM + Plan §6.1 & ADR-001 §3.3 BLOCKER-2 | `soap11/echo/echo-dcs-utf8bom` |
| `SOAP11-HEADER-MUSTUNDERSTAND-OTHER-ACTOR` | SOAP 1.1 | `DataContractSerializer` | `s:mustUnderstand="1"` with `s:actor="http://example.org/intermediary"`; **must NOT fault** at ultimate receiver (FastSoap HTTP `200`; note CoreWCF `1.9.2` default `ValidateMustUnderstand=true` returns HTTP `500` recorded in `.corewcf-response.bin`) | `supported` | SOAP 1.1 §4.2.2/§4.2.3 + Plan §6.3 & ADR-001 §3.3 BLOCKER-2 | `soap11/headers/echo-mustunderstand-other-actor` |
| `SOAP12-HEADER-MUSTUNDERSTAND-ROLE-NONE` | SOAP 1.2 | `DataContractSerializer` | `s:mustUnderstand="true"` with `s:role="http://www.w3.org/2003/05/soap-envelope/role/none"`; **must NOT fault** (FastSoap HTTP `200`; note CoreWCF `1.9.2` default `ValidateMustUnderstand=true` returns HTTP `500` recorded in `.corewcf-response.bin`) | `supported` | SOAP 1.2 Part 1 §2.2/§2.4 + Plan §6.3 & ADR-001 §3.3 BLOCKER-2 | `soap12/headers/echo-mustunderstand-role-none` |

---

## 4. BCL Reference Serializer Payload Fixtures (`tests/fixtures/serializers/`)

Standalone payload XML fragments (UTF-8 without BOM, no XML declaration, unindented) produced directly by BCL `System.Runtime.Serialization.DataContractSerializer` and `System.Xml.Serialization.XmlSerializer`.

| Fixture ID | Serializer | Root Element & Namespace | Wire Notes | Expected Outcome | CoreWCF Test-Suite Mapping | File |
|---|---|---|---|---|---|---|
| `SER-DCS-ECHO` | `DataContractSerializer` | `<string xmlns="http://schemas.microsoft.com/2003/10/Serialization/">` | Primitive string payload | `supported` | `CoreWCF.DataContractSerialization.TestCorpus.SanityPrimitives` + `SerializationTests` | `serializers/dcs/echo-string.xml` |
| `SER-DCS-TRANSFER-DEFAULT` | `DataContractSerializer` | `<TransferRequestDefaultDcs xmlns="http://schemas.datacontract.org/2004/07/FastSoap.R0G.Oracle.Contracts">` | Implicit CLR namespace, alphabetical fields, `i:nil="true"` | `supported` | `CoreWCF.Http.Tests.ComplexSerializationTests.DataContractNameSpaceComplexSerialization` | `serializers/dcs/transfer-request-default.xml` |
| `SER-DCS-TRANSFER-URN` | `DataContractSerializer` | `<TransferRequest xmlns="urn:payments">` | Explicit `Order=0..4`, nested `Metadata` | `supported` | `CoreWCF.DataContractSerialization.TestCorpus.SanityCustomNaming` + `SanityMemberAttributes` | `serializers/dcs/transfer-request-urnpayments.xml` |
| `SER-DCS-TRANSFER-NIL` | `DataContractSerializer` | `<TransferRequest xmlns="urn:payments">` | `i:nil="true"` on `Metadata` and `Note` | `supported` | `CoreWCF.DataContractSerialization.TestCorpus.SanityNullable` | `serializers/dcs/transfer-request-urnpayments-nil.xml` |
| `SER-DCS-TRANSFER-RESULT` | `DataContractSerializer` | `<TransferResult xmlns="urn:payments">` | UTC `xs:dateTime` + string enum `Completed` | `supported` | `CoreWCF.DataContractSerialization.TestCorpus.SanityEnums` + `SanityDateAndTimeOnly` | `serializers/dcs/transfer-result-urnpayments.xml` |
| `SER-DCS-BATCH-TRANSFER` | `DataContractSerializer` | `<BatchTransferRequest xmlns="urn:payments">` | Nested `Items/TransferRequest` collection | `supported` | `CoreWCF.DataContractSerialization.TestCorpus.SanityCollections` | `serializers/dcs/batch-transfer-request.xml` |
| `SER-DCS-PAYMENT-FAULT` | `DataContractSerializer` | `<PaymentFaultDetail xmlns="urn:payments">` | Typed fault detail payload | `supported` | `CoreWCF.Http.Tests.FaultContractTests.DatacontractFaults` | `serializers/dcs/payment-fault-detail.xml` |
| `SER-XML-ECHO` | `XmlSerializer` | `<string>` | No namespace on standalone primitive `string` | `supported` | `CoreWCF.Http.Tests.ServiceWithCoreWCFXmlSerializerFormatTest` | `serializers/xmlserializer/echo-string.xml` |
| `SER-XML-TRANSFER-URN` | `XmlSerializer` | `<TransferRequest xmlns:xsi="..." xmlns:xsd="...">` | Child elements qualified with `xmlns="urn:payments"` | `supported` | `CoreWCF.Http.Tests.ServiceWithCoreWCFXmlSerializerFormatTest` + Plan §3.2 | `serializers/xmlserializer/transfer-request-urnpayments.xml` |
| `SER-XML-TRANSFER-NIL` | `XmlSerializer` | `<TransferRequest ...>` | `xsi:nil="true"` on `Metadata` and `Note` | `supported` | `CoreWCF.Http.Tests.ServiceWithCoreWCFXmlSerializerFormatTest` (`IsNullable=true`) | `serializers/xmlserializer/transfer-request-urnpayments-nil.xml` |
| `SER-XML-TRANSFER-RESULT` | `XmlSerializer` | `<TransferResult ...>` | Ordered child elements in `urn:payments` | `supported` | `CoreWCF.Http.Tests.ServiceWithCoreWCFXmlSerializerFormatTest` + Plan §3.2 | `serializers/xmlserializer/transfer-result-urnpayments.xml` |
| `SER-XML-BATCH-TRANSFER` | `XmlSerializer` | `<BatchTransferRequest ...>` | `<Items><TransferRequest>...</TransferRequest></Items>` array shape | `supported` | `CoreWCF.Http.Tests.ServiceWithCoreWCFXmlSerializerFormatTest` | `serializers/xmlserializer/batch-transfer-request.xml` |

---

## 5. WSDL 1.1 & XSD Metadata Fixtures (`tests/fixtures/wsdl/`)

Captured from CoreWCF `ServiceMetadataBehavior` (`?singleWsdl`, `?wsdl`, `?wsdl=wsdl0`, `?xsd=xsd0`, `?xsd=xsd1`) with ephemeral `localhost:<port>` normalized to `localhost:5000`.

| Fixture ID | Version | Serializer | Artifacts | Expected Outcome | CoreWCF Test-Suite Mapping |
|---|---|---|---|---|---|
| `WSDL-ECHO-SOAP11` | SOAP 1.1 | `DataContractSerializer` | `echo-soap11.single.wsdl`, `echo-soap11.wsdl`, `echo-soap11.xsd0.xsd`, `echo-soap11.xsd1.xsd` | `supported` | `CoreWCF.Metadata.Tests.BasicHttpSimpleServiceTest.BasicHttpRequestReplyEchoString` |
| `WSDL-PAYMENT-DCS-SOAP11` | SOAP 1.1 | `DataContractSerializer` | `payment-dcs-soap11.single.wsdl`, `payment-dcs-soap11.wsdl`, `payment-dcs-soap11.wsdl0.wsdl`, `payment-dcs-soap11.xsd0.xsd`, `payment-dcs-soap11.xsd1.xsd` | `supported` | `CoreWCF.Metadata.Tests.ComplexTypesWithCollectionsService` + `DataTypesTest` |
| `WSDL-PAYMENT-XMLSER-SOAP11` | SOAP 1.1 | `XmlSerializer` | `payment-xmlser-soap11.single.wsdl` | `supported` | `CoreWCF.Metadata.Tests.XmlSerializerFormatSoapTest` |
| `WSDL-PAYMENT-DCS-SOAP12` | SOAP 1.2 | `DataContractSerializer` | `payment-dcs-soap12.single.wsdl` (`soap12:binding`, `soap12:operation`, `soap12:fault`, `soap12:address`) | `supported` | `CoreWCF.Metadata.Tests.Helpers.WsdlHelper` (`soap12` extensions) |

---

## 6. Adversarial & Negative XML/Protocol Fixtures (`tests/fixtures/negative/`)

Every negative vector was transmitted verbatim to the live CoreWCF `1.9.2` Kestrel server (`net10.0`) to record CoreWCF's actual HTTP status code and response body (`.corewcf-response.bin`) alongside FastSoap's security/protocol specification (§6, §8, §11, ADR-001 §3.2 & §3.3 BLOCKER-2). All 27 entries are `excluded` (must be rejected with **zero** business service invocations).

| Fixture ID | Attack / Protocol Violation | Observed CoreWCF 1.9.2 Response | Expected FastSoap Outcome | CoreWCF Test-Suite Mapping | Files |
|---|---|---|---|---|---|
| `NEG-01` | DTD / XXE `<!DOCTYPE foo [<!ENTITY xxe SYSTEM "file:///etc/passwd">]>` | HTTP `400`, 0 bytes | Reject before service invocation (DTD prohibited, HTTP `400` / Sender fault) | `CoreWCF` `TextMessageEncoder` / `XmlDictionaryReaderQuotas` (`DtdProcessing.Prohibit`) + Plan §11 | `negative/neg01-dtd-xxe.*` |
| `NEG-02` | DTD Entity Expansion (Billion Laughs) | HTTP `400`, 0 bytes | Reject before service invocation (DTD prohibited, HTTP `400`) | `CoreWCF` `TextMessageEncoder` / `XmlDictionaryReaderQuotas` (`DtdProcessing.Prohibit`) + Plan §11 | `negative/neg02-dtd-entity-expansion.*` |
| `NEG-03A` | Malformed UTF-8 bytes (`0xFF 0xFE 0xC3 0x28`) in body | HTTP `500`, `a:DeserializationFailed` fault | Reject invalid UTF-8 bytes before service invocation (HTTP `400`) | `CoreWCF` `TextMessageEncoder` UTF-8 decoding + Plan §6.1 & §11 | `negative/neg03a-malformed-utf8.*` |
| `NEG-03B` | Invalid XML 1.0 control byte (`0x0F`) in element text | HTTP `500`, `a:DeserializationFailed` (`The byte 0x0F is not valid at this location`) | Reject invalid XML 1.0 character before service invocation (HTTP `400`) | `CoreWCF.Primitives.Tests.MessageTests.InvalidCharToString` | `negative/neg03b-invalid-xml10-char.*` |
| `NEG-04` | Duplicate `<s:Body>` element in SOAP 1.1 Envelope | HTTP `500`, `a:DeserializationFailed` (`End element 'Envelope' ... expected. Found element 's:Body'`) | Reject duplicate Body per SOAP 1.1 grammar (HTTP `400` / Client fault) | `CoreWCF` Envelope reader validation + Plan §6.2 | `negative/neg04-duplicate-body-soap11.*` |
| `NEG-05` | Wrong ordering (`<s:Header>` after `<s:Body>`) | HTTP `500`, `a:DeserializationFailed` (`Found element 's:Header'`) | Reject Header after Body per SOAP 1.1 grammar (HTTP `400` / Client fault) | `CoreWCF` Envelope reader validation + Plan §6.2 | `negative/neg05-wrong-ordering-soap11.*` |
| `NEG-06A` | Unknown mandatory header (`s:mustUnderstand="1"`, SOAP 1.1) | HTTP `500`, `a:InternalServiceFault` (with `IncludeExceptionDetailInFaults=false`) | SOAP 1.1 `s:MustUnderstand` fault (HTTP `500`), zero service invocations | `CoreWCF` `MessageHeaders.UnderstoodHeaders` / `MustUnderstand` + Plan §6.3 | `negative/neg06a-unknown-mustunderstand-soap11.*` |
| `NEG-06B` | Unknown mandatory header (`s:mustUnderstand="true"`, SOAP 1.2) | HTTP `500`, `s:Receiver` / `a:InternalServiceFault` | SOAP 1.2 `s:MustUnderstand` fault + `NotUnderstood` header, zero service invocations | `CoreWCF` SOAP 1.2 `MustUnderstand` + Plan §6.3 | `negative/neg06b-unknown-mustunderstand-soap12.*` |
| `NEG-07` | Action / Body QName mismatch (`SOAPAction: Transfer` with `<EchoString>`) | HTTP `500`, `a:InternalServiceFault` | Reject action/body QName mismatch with SOAP fault, zero service invocations | `CoreWCF.Http.Tests.OpActionReplyActionBehaviorTests` + Plan §4 & §6.1 | `negative/neg07-action-body-mismatch-soap11.*` |
| `NEG-08` | Oversized payload (70 KiB > default 65,536 `MaxReceivedMessageSize`) | HTTP `413`, 0 bytes | Reject payload exceeding `MaxRequestBytes` (HTTP `413 PayloadTooLarge`) | `CoreWCF.Http.Tests.RequestDelegateHandlerMaxBodySizeTests` & `LargeRequestTests` | `negative/neg08-oversized-payload-soap11.*` |
| `NEG-09` | Non-UTF-8 encoding (`text/xml; charset=utf-16` + `encoding="utf-16"` with UTF-8 bytes) | HTTP `400`, 0 bytes | Reject non-UTF-8 charset / declaration mismatch (HTTP `415 UnsupportedMediaType`) | `CoreWCF` `TextMessageEncoder` UTF-8 enforcement + Plan §6.1 | `negative/neg09-utf16-encoding-mismatch.*` |
| `NEG-10` | Non-Envelope XML root (`<Transfer xmlns="urn:payments">`) | HTTP `400`, 0 bytes | Reject non-Envelope root element (HTTP `400` / Client fault) | `CoreWCF` `Message.CreateMessage` envelope check + Plan §6.2 | `negative/neg10-non-envelope-root.*` |
| `NEG-11` | Trailing root element after `</s:Envelope>` (`<TrailingRoot />`) | HTTP `500`, `a:DeserializationFailed` (`There are multiple root elements`) | Reject trailing root content (HTTP `400`), zero service invocations | `CoreWCF` `XmlReader` trailing content rejection + Plan §5.2 & §6.2 | `negative/neg11-trailing-root.*` |
| `NEG-12` | Missing `SOAPAction` HTTP header on SOAP 1.1 POST | HTTP `500`, `a:ActionNotSupported` | Reject missing `SOAPAction` in strict SOAP 1.1 mode (HTTP `400` / Client fault) | `CoreWCF` `BasicHttpBinding` `Action` dispatch + Plan §6.1 | `negative/neg12-missing-soapaction-soap11.*` |
| `NEG-13` | Version mismatch (SOAP 1.2 envelope posted to SOAP 1.1 endpoint) | HTTP `400`, 0 bytes | SOAP `s:VersionMismatch` fault (HTTP `500`) or HTTP `400/415`, zero service invocations | `CoreWCF.Http.Tests.MessageVersionsTests` + Plan §6.1 & §6.4 | `negative/neg13-version-mismatch-soap12-on-soap11.*` |
| `NEG-14A` | Processing instruction (`<?pi envelope-instruction?>`) inside `<s:Envelope>` | HTTP `400`, 0 bytes | Reject processing instruction inside SOAP envelope (HTTP `400`), zero service invocations | SOAP 1.1 §3 / SOAP 1.2 Part 1 §5 + Plan §6.2 & ADR-001 §3.3 | `negative/neg14a-pi-in-envelope.*` |
| `NEG-14B` | Processing instruction (`<?pi body-instruction?>`) inside deserialized body element | HTTP `500`, `a:DeserializationFailed` (`Processing instructions ... are not supported`) | Reject processing instruction inside deserialized body element (HTTP `400`), zero service invocations | SOAP 1.1 §3 / SOAP 1.2 Part 1 §5 + ADR-001 §3.2 #1(h) & §3.3 | `negative/neg14b-pi-in-body.*` |
| `NEG-15` | XML 1.1 declaration (`<?xml version='1.1' encoding='utf-8'?>`) | HTTP `400`, 0 bytes | Reject XML 1.1 declaration (XML 1.0 only per Plan §6.2, HTTP `400`), zero service invocations | `CoreWCF` `TextMessageEncoder` (XML 1.0 only) + Plan §6.2 & ADR-001 §3.3 | `negative/neg15-xml11-declaration.*` |
| `NEG-16A` | Truncated UTF-8 BOM (`0xEF 0xBB`) before `<s:Envelope>` | HTTP `400`, 0 bytes | Reject invalid/truncated UTF-8 BOM prefix (HTTP `400`), zero service invocations | `CoreWCF` `TextMessageEncoder` UTF-8 BOM validation + Plan §6.1 & ADR-001 §3.3 | `negative/neg16a-truncated-utf8-bom.*` |
| `NEG-16B` | UTF-16 LE BOM (`0xFF 0xFE`) with `text/xml; charset=utf-8` | HTTP `400`, 0 bytes | Reject non-UTF-8 BOM mismatching `charset=utf-8` (HTTP `400` / `415`), zero service invocations | `CoreWCF` `TextMessageEncoder` BOM/charset validation + Plan §6.1 & ADR-001 §3.3 | `negative/neg16b-utf16-bom-utf8-charset.*` |
| `NEG-17A` | `s:encodingStyle` attribute on `<s:Envelope>` | HTTP `200` (CoreWCF ignores `encodingStyle`) | Reject `soap:encodingStyle` on Envelope (document/literal only per Plan §6.2 & ADR-001 §3.2, HTTP `400`) | Plan §1.1 & §6.2 + ADR-001 §3.2 #2 & §3.3 BLOCKER-2 | `negative/neg17a-encoding-style-envelope-soap11.*` |
| `NEG-17B` | `s:encodingStyle` attribute on `<s:Body>` | HTTP `200` (CoreWCF ignores `encodingStyle`) | Reject `soap:encodingStyle` on Body (document/literal only per Plan §6.2 & ADR-001 §3.2, HTTP `400`) | Plan §1.1 & §6.2 + ADR-001 §3.2 #2 & §3.3 BLOCKER-2 | `negative/neg17b-encoding-style-body-soap11.*` |
| `NEG-18A` | Excessive XML element depth (70 nested `<d>` elements > `MaxXmlDepth` 64) | HTTP `500`, `a:DeserializationFailed` (`maximum depth quota (32)`) | Reject XML nesting exceeding `MaxXmlDepth` (64) before service invocation (HTTP `400`) | `CoreWCF` `XmlDictionaryReaderQuotas.MaxDepth` + Plan §8 & ADR-001 §3.3 | `negative/neg18a-excessive-depth-soap11.*` |
| `NEG-18B` | Excessive attributes per element (70 attributes > `MaxAttributesPerElement` 64) | HTTP `200` (CoreWCF has no attribute count quota) | Reject element exceeding `MaxAttributesPerElement` (64) before service invocation (HTTP `400`) | Plan §8 & ADR-001 §3.2 #1 (`MaxAttributeCount=64`) & §3.3 | `negative/neg18b-excessive-attributes-soap11.*` |
| `NEG-18C` | Excessive SOAP header count (40 `<x:Trace>` headers > `MaxHeaderCount` 32) | HTTP `200` (CoreWCF has no header count quota) | Reject SOAP Envelope exceeding `MaxHeaderCount` (32) before service invocation (HTTP `400`) | Plan §6.3 & §8 (`MaxHeaderCount=32`) + ADR-001 §3.2 #2 & §3.3 | `negative/neg18c-excessive-header-count-soap11.*` |
| `NEG-18D` | Oversized SOAP header block (~68 KiB > `MaxHeaderBytes` 64 KiB) | HTTP `413`, 0 bytes | Reject SOAP Header block exceeding `MaxHeaderBytes` (64 KiB) before service invocation (HTTP `400` / `413`) | Plan §6.3 & §8 (`MaxHeaderBytes=64KiB`) + ADR-001 §3.2 #2 & §3.3 | `negative/neg18d-oversized-header-block-soap11.*` |
| `NEG-19` | Duplicate `<AccountNumber>` XML element in `TransferRequest` DTO payload | HTTP `200` (`XmlSerializer` silently accepts duplicate element) | Reject duplicate XML element in DTO payload (`AllowDuplicateProperties=false` per Plan §7.2 & ADR-001 §3.2, HTTP `400`) | Plan §7.2 & §11 (duplicate fields) + ADR-001 §3.2 #1 & §3.3 | `negative/neg19-duplicate-dto-element-soap11.*` |

---

## 7. CoreWCF Test-Suite Scenarios Explicitly Excluded or Pending Outside v0.1

To prevent false compatibility claims, the following upstream CoreWCF `v1.9.2` test suites are **not** included as supported v0.1 targets:

| CoreWCF v1.9.2 Test Suite | Feature | Classification | Reason |
|---|---|---|---|
| `MessageVersionsTests` (`Soap11WSAddressing10`, `Soap12WSAddressing10`, `Soap12WSAddressingAugust2004`) | WS-Addressing headers & EPR routing | `excluded` | FastSoap Plan §1.1 explicitly defers WS-Addressing beyond v0.1 |
| `MessageEncoderTests` (`BinaryMessageEncoderCompressionFormat_EchoString`) | WCF Binary Message Encoder (GZip/Deflate/None) | `excluded` | FastSoap v0.1 targets UTF-8 text XML (`text/xml` and `application/soap+xml`) |
| `StreamingServiceTests` / `IStreamingService` | Unbuffered `Stream` body transfer | `excluded` | FastSoap v0.1 uses bounded buffered request/response ownership (ADR-004, ADR-005) |
| `ComplexSerializationTests.ISerializableComplexSerialization` / `IXMLSerializableComplexSerialization` | Legacy `ISerializable` & `IXmlSerializable` | `excluded` | Reflection/runtime-type hooks incompatible with zero-reflection Native AOT |
| `CoreWCF.Metadata.Tests.XmlSerializerFormatSoapTest` (`OperationFormatUse.Encoded`) | RPC/Encoded (`soapenc`) | `excluded` | FastSoap Plan §1.1 & ADR-007 exclude RPC/Encoded |
| `BasicHttpTransportMessage` (`BasicHttpSecurityMode.TransportWithMessageCredential`) | WS-Security Username/X509 message tokens | `excluded` | FastSoap Plan §1.1 & §11 exclude WS-Security in v0.1 |
| `CoreWCF.DataContractSerialization` PR `#1766` `SanityReferenceNode` (`IsReference=true` cycles) | `z:Id` / `z:Ref` cyclic object graphs | `pending` | Requires explicit cycle/reference-table support if prioritized post-M1 |

---

## 8. Reproduction Commands

Verify all 63 immutable golden fixtures on disk against live CoreWCF `1.9.2` + `System.ServiceModel.Http` `10.0.652802` + BCL serializers on `net10.0`:

```pwsh
dotnet restore experiments/R0G-Oracle/FastSoap.R0G.Oracle.csproj
dotnet test experiments/R0G-Oracle/FastSoap.R0G.Oracle.csproj -c Release --no-restore
```

Regenerate all fixtures in `tests/fixtures/` from the live oracle (only when intentionally updating the corpus):

```pwsh
$env:FASTSOAP_REGENERATE_FIXTURES="1"
dotnet test experiments/R0G-Oracle/FastSoap.R0G.Oracle.csproj -c Release
Remove-Item Env:FASTSOAP_REGENERATE_FIXTURES
```
