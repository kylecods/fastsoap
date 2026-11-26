using Microsoft.CodeAnalysis;

namespace FastSoap.Generators
{

    /// <summary>
    /// Defines compile-time <see cref="DiagnosticDescriptor"/> metadata for FastSoap contract and wire-mapping errors.
    /// </summary>
    internal static class DiagnosticDescriptors
    {

        #region Fields

        private const string Category = "FastSoap.Contract";

        /// <summary>
        /// FS0001: Service contract missing or invalid namespace or name.
        /// </summary>
        internal static readonly DiagnosticDescriptor ServiceContractMissingOrInvalid = new DiagnosticDescriptor(
            id: "FS0001",
            title: "Service contract missing or invalid namespace or name",
            messageFormat: "SOAP service contract '{0}' {1}",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "A [SoapService] contract must declare a valid non-empty XML Namespace and a valid NCName/C# identifier Name.");

        /// <summary>
        /// FS0002: Duplicate or ambiguous action or body element mapping.
        /// </summary>
        internal static readonly DiagnosticDescriptor DuplicateOrAmbiguousMapping = new DiagnosticDescriptor(
            id: "FS0002",
            title: "Duplicate or ambiguous action or body mapping",
            messageFormat: "SOAP operation '{0}' on service '{1}' has a duplicate or ambiguous mapping: {2}",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Each SOAP operation on a service contract must have a unique action URI, operation name, and request Body qualified name.");

        /// <summary>
        /// FS0003: Unsupported method signature, open generic, ref/out, or overload.
        /// </summary>
        internal static readonly DiagnosticDescriptor UnsupportedMethodSignature = new DiagnosticDescriptor(
            id: "FS0003",
            title: "Unsupported method signature, open generic, ref/out, or overload",
            messageFormat: "SOAP operation '{0}' has an unsupported signature: {1}",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "SOAP service types cannot be open generics, and operations must be non-overloaded, non-generic async methods returning Task<T> or ValueTask<T> with one input DTO and optional trailing CancellationToken.");

        /// <summary>
        /// FS0004: Missing explicit request or response wire DTO mapping.
        /// </summary>
        internal static readonly DiagnosticDescriptor MissingExplicitWireMapping = new DiagnosticDescriptor(
            id: "FS0004",
            title: "Missing explicit request or response wire DTO mapping",
            messageFormat: "SOAP operation '{0}' is missing valid explicit wire DTO mapping: {1}",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Each [SoapOperation] method must declare [SoapWireContract] with valid RequestType, ResponseType, and matching member mappings.");

        /// <summary>
        /// FS0005: Missing or incompatible XML serializer context declaration.
        /// </summary>
        internal static readonly DiagnosticDescriptor MissingOrIncompatibleXmlContext = new DiagnosticDescriptor(
            id: "FS0005",
            title: "Missing or incompatible XML serializer context declaration",
            messageFormat: "SOAP operation '{0}' has a missing or incompatible XML serializer context declaration: {1}",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "The resolved ContextType must inherit from XmlSerializerContext and declare [XmlSerializable<T>] metadata for both RequestType and ResponseType.");

        /// <summary>
        /// FS0006: Invalid XML QName or unsupported wire schema shape.
        /// </summary>
        internal static readonly DiagnosticDescriptor InvalidQNameOrWireShape = new DiagnosticDescriptor(
            id: "FS0006",
            title: "Invalid XML QName or unsupported wire schema shape",
            messageFormat: "SOAP contract member '{0}' has an invalid XML QName or unsupported wire schema shape: {1}",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Wire DTOs and operation element names must form valid XML qualified names and represent concrete serializable shapes with [XmlRoot(Namespace = ...)].");

        /// <summary>
        /// FS0007: Inaccessible service type or member.
        /// </summary>
        internal static readonly DiagnosticDescriptor InaccessibleServiceOrMember = new DiagnosticDescriptor(
            id: "FS0007",
            title: "Inaccessible service type or member",
            messageFormat: "SOAP contract symbol '{0}' is inaccessible for generated registration: {1}",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "The service contract, operation methods, wire DTO types, wrapper properties, and XmlSerializerContext must be accessible from generated code.");

        /// <summary>
        /// FS0008: Conflicting generated registration method name.
        /// </summary>
        internal static readonly DiagnosticDescriptor ConflictingRegistrationMethodName = new DiagnosticDescriptor(
            id: "FS0008",
            title: "Conflicting generated registration method name",
            messageFormat: "Generated registration method '{0}' for service '{1}' conflicts with {2}",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Each [SoapService] contract must produce a unique registration method name Map<ServiceName> without colliding with other generated or authored methods.");

        #endregion

    }

}
