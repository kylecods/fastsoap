; Unshipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
FS0001 | FastSoap.Contract | Error | Service contract missing/invalid namespace or name
FS0002 | FastSoap.Contract | Error | Duplicate or ambiguous action/body mapping
FS0003 | FastSoap.Contract | Error | Unsupported method signature, open generic, ref/out, or overload
FS0004 | FastSoap.Contract | Error | Missing explicit request/response wire DTO mapping
FS0005 | FastSoap.Contract | Error | Missing/incompatible XML context declaration
FS0006 | FastSoap.Contract | Error | Invalid XML QName / unsupported wire schema shape
FS0007 | FastSoap.Contract | Error | Inaccessible service type or member
FS0008 | FastSoap.Contract | Error | Conflicting generated registration method name
