using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using FastSoap.Abstractions;

namespace FastSoap.Metadata
{

    /// <summary>
    /// Defines an immutable, validated contract-first SOAP service metadata model
    /// used by <see cref="SoapWsdlGenerator"/> to emit deterministic WSDL 1.1 and XSD artifacts.
    /// </summary>
    /// <example>
    /// <code>
    /// SoapServiceMetadata service = new SoapServiceMetadata(
    ///     serviceName: "PaymentService",
    ///     targetNamespace: "urn:payments",
    ///     operations: [transferOperation],
    ///     supportedVersions: SoapVersionFlags.Both,
    ///     portTypeName: "IPaymentService");
    /// </code>
    /// </example>
    /// <remarks>
    /// Validates service and portType NCNames, qualified target namespaces, supported SOAP version flags,
    /// duplicate operations/actions/body elements, and conflicting schema type definitions fail-first.
    /// </remarks>
    public sealed class SoapServiceMetadata
    {

        #region Fields

        private static readonly FrozenSet<string> ReservedServiceNamespaces = new[]
        {
            SoapSchemaType.XmlSchemaNamespace,
            "http://www.w3.org/2001/XMLSchema-instance",
            "http://schemas.xmlsoap.org/wsdl/",
            "http://schemas.xmlsoap.org/wsdl/soap/",
            "http://schemas.xmlsoap.org/wsdl/soap12/",
            "http://schemas.xmlsoap.org/soap/encoding/",
            "http://schemas.xmlsoap.org/soap/envelope/",
            "http://www.w3.org/2003/05/soap-envelope"
        }.ToFrozenSet(StringComparer.Ordinal);

        #endregion

        #region Properties

        /// <summary>
        /// Gets the ordered list of operations exposed by this service.
        /// </summary>
        public IReadOnlyList<SoapOperationMetadata> Operations { get; }

        /// <summary>
        /// Gets the WSDL <c>portType</c> NCName.
        /// </summary>
        public string PortTypeName { get; }

        /// <summary>
        /// Gets the WSDL <c>definitions</c> and <c>service</c> NCName.
        /// </summary>
        public string ServiceName { get; }

        /// <summary>
        /// Gets the SOAP protocol versions advertised in WSDL bindings and service ports.
        /// </summary>
        public SoapVersionFlags SupportedVersions { get; }

        /// <summary>
        /// Gets the service's qualified WSDL <c>targetNamespace</c>.
        /// </summary>
        public string TargetNamespace { get; }

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new validated <see cref="SoapServiceMetadata"/> contract model.
        /// </summary>
        /// <param name="serviceName">The valid XML NCName for the WSDL service.</param>
        /// <param name="targetNamespace">The qualified WSDL <c>targetNamespace</c>.</param>
        /// <param name="operations">The non-empty list of unique service operations.</param>
        /// <param name="supportedVersions">Enabled SOAP versions (<see cref="SoapVersionFlags.Soap11"/>, <see cref="SoapVersionFlags.Soap12"/>, or <see cref="SoapVersionFlags.Both"/>).</param>
        /// <param name="portTypeName">Optional WSDL <c>portType</c> NCName; defaults to <c>I{serviceName}</c>.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="operations"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="supportedVersions"/> is <see cref="SoapVersionFlags.None"/> or invalid.</exception>
        /// <exception cref="ArgumentException">Thrown when names, namespaces, operations, or schema type definitions are invalid or duplicated.</exception>
        public SoapServiceMetadata(
            string serviceName,
            string targetNamespace,
            IReadOnlyList<SoapOperationMetadata> operations,
            SoapVersionFlags supportedVersions = SoapVersionFlags.Soap11,
            string? portTypeName = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetNamespace);
            ArgumentNullException.ThrowIfNull(operations);

            SoapQName validatedServiceQName = new SoapQName(targetNamespace, serviceName);
            if (ReservedServiceNamespaces.Contains(validatedServiceQName.NamespaceUri))
            {
                throw new ArgumentException(
                    $"Service targetNamespace '{validatedServiceQName.NamespaceUri}' cannot be a reserved protocol namespace.",
                    nameof(targetNamespace));
            }

            string resolvedPortTypeName = portTypeName ?? $"I{validatedServiceQName.LocalName}";
            SoapQName validatedPortTypeQName = new SoapQName(validatedServiceQName.NamespaceUri, resolvedPortTypeName);

            if (supportedVersions == SoapVersionFlags.None || (supportedVersions & ~SoapVersionFlags.Both) != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(supportedVersions),
                    supportedVersions,
                    "SupportedVersions must enable Soap11, Soap12, or Both.");
            }

            if (operations.Count == 0)
            {
                throw new ArgumentException("Service metadata must define at least one operation.", nameof(operations));
            }

            SoapOperationMetadata[] operationsCopy = new SoapOperationMetadata[operations.Count];
            HashSet<string> seenNames = new HashSet<string>(operations.Count, StringComparer.Ordinal);
            HashSet<string> seenActions = new HashSet<string>(operations.Count, StringComparer.Ordinal);
            HashSet<SoapQName> seenRequestElements = new HashSet<SoapQName>(operations.Count);
            HashSet<SoapQName> seenResponseElements = new HashSet<SoapQName>(operations.Count);
            Dictionary<SoapQName, SoapSchemaElement> globalElements = new Dictionary<SoapQName, SoapSchemaElement>();
            Dictionary<SoapQName, SoapSchemaType> namedTypes = new Dictionary<SoapQName, SoapSchemaType>();

            for (int index = 0; index < operations.Count; index++)
            {
                SoapOperationMetadata operation = operations[index];
                ArgumentNullException.ThrowIfNull(operation);

                SoapOperationDescriptor descriptor = operation.Descriptor;
                if (!seenNames.Add(descriptor.Name))
                {
                    throw new ArgumentException($"Duplicate operation name '{descriptor.Name}'.", nameof(operations));
                }

                if (!seenActions.Add(descriptor.Action))
                {
                    throw new ArgumentException($"Duplicate SOAP action '{descriptor.Action}'.", nameof(operations));
                }

                if (!seenRequestElements.Add(descriptor.RequestElement))
                {
                    throw new ArgumentException(
                        $"Duplicate request Body element '{descriptor.RequestElement}'.",
                        nameof(operations));
                }

                if (!seenResponseElements.Add(descriptor.ResponseElement))
                {
                    throw new ArgumentException(
                        $"Duplicate response Body element '{descriptor.ResponseElement}'.",
                        nameof(operations));
                }

                RegisterGlobalElement(operation.RequestSchema, globalElements, namedTypes);
                RegisterGlobalElement(operation.ResponseSchema, globalElements, namedTypes);
                operationsCopy[index] = operation;
            }

            ServiceName = validatedServiceQName.LocalName;
            PortTypeName = validatedPortTypeQName.LocalName;
            TargetNamespace = validatedServiceQName.NamespaceUri;
            SupportedVersions = supportedVersions;
            Operations = operationsCopy;
        }

        #endregion

        #region Private Methods

        private static void RegisterGlobalElement(
            SoapSchemaElement element,
            Dictionary<SoapQName, SoapSchemaElement> globalElements,
            Dictionary<SoapQName, SoapSchemaType> namedTypes)
        {
            if (globalElements.TryGetValue(element.QName, out SoapSchemaElement? existing))
            {
                if (!existing.Equals(element))
                {
                    throw new ArgumentException(
                        $"Conflicting global schema element definitions for '{element.QName}'.",
                        nameof(element));
                }
            }
            else
            {
                globalElements.Add(element.QName, element);
            }

            if (element.Type is not null)
            {
                RegisterSchemaType(element.Type, namedTypes);
            }

            for (int index = 0; index < element.InlineSequence.Count; index++)
            {
                SoapSchemaElement member = element.InlineSequence[index];
                if (member.Type is not null)
                {
                    RegisterSchemaType(member.Type, namedTypes);
                }
            }
        }

        private static void RegisterSchemaType(
            SoapSchemaType schemaType,
            Dictionary<SoapQName, SoapSchemaType> namedTypes)
        {
            if (schemaType.Kind == SoapSchemaTypeKind.Primitive)
            {
                return;
            }

            if (namedTypes.TryGetValue(schemaType.QName, out SoapSchemaType? existing))
            {
                if (!existing.Equals(schemaType))
                {
                    throw new ArgumentException(
                        $"Conflicting schema type definitions for '{schemaType.QName}'.",
                        nameof(schemaType));
                }

                return;
            }

            namedTypes.Add(schemaType.QName, schemaType);

            for (int index = 0; index < schemaType.SequenceMembers.Count; index++)
            {
                SoapSchemaElement member = schemaType.SequenceMembers[index];
                if (member.Type is not null)
                {
                    RegisterSchemaType(member.Type, namedTypes);
                }
            }
        }

        #endregion

    }

}
