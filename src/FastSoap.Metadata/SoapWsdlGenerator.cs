using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CloudNimble.Text.Xml;
using FastSoap.Abstractions;

namespace FastSoap.Metadata
{

    /// <summary>
    /// Emits deterministic UTF-8 WSDL 1.1 and XML Schema documents directly via <see cref="Utf8XmlWriter"/>
    /// for FastSoap's supported document/literal subset.
    /// </summary>
    /// <example>
    /// <code>
    /// SoapMetadataAddressPolicy addressPolicy = new SoapMetadataAddressPolicy("https://api.example.com/PaymentService.svc");
    /// byte[] wsdlBytes = SoapWsdlGenerator.GenerateWsdl(serviceMetadata, addressPolicy);
    /// SoapStaticMetadataProvider provider = SoapWsdlGenerator.GenerateStaticProvider(serviceMetadata, addressPolicy);
    /// </code>
    /// </example>
    /// <remarks>
    /// Supports SOAP 1.1 (<c>soap:binding</c>), SOAP 1.2 (<c>soap12:binding</c>), or both simultaneously with distinct bindings and ports.
    /// Never emits unsupported encodings, headers, faults, or WS-* policy claims, and never uses BCL <c>System.Xml</c>.
    /// </remarks>
    public static class SoapWsdlGenerator
    {

        #region Fields

        private const string Soap11WsdlNamespace = "http://schemas.xmlsoap.org/wsdl/soap/";
        private const string Soap12WsdlNamespace = "http://schemas.xmlsoap.org/wsdl/soap12/";
        private const string SoapHttpTransport = "http://schemas.xmlsoap.org/soap/http";
        private const string WsdlNamespace = "http://schemas.xmlsoap.org/wsdl/";
        private const string XmlSchemaNamespace = SoapSchemaType.XmlSchemaNamespace;

        private static ReadOnlySpan<byte> Soap11PrefixUtf8 => "soap"u8;
        private static ReadOnlySpan<byte> Soap11WsdlNamespaceUtf8 => "http://schemas.xmlsoap.org/wsdl/soap/"u8;
        private static ReadOnlySpan<byte> Soap12PrefixUtf8 => "soap12"u8;
        private static ReadOnlySpan<byte> Soap12WsdlNamespaceUtf8 => "http://schemas.xmlsoap.org/wsdl/soap12/"u8;
        private static ReadOnlySpan<byte> WsdlNamespaceUtf8 => "http://schemas.xmlsoap.org/wsdl/"u8;
        private static ReadOnlySpan<byte> WsdlPrefixUtf8 => "wsdl"u8;
        private static ReadOnlySpan<byte> XsPrefixUtf8 => "xs"u8;
        private static ReadOnlySpan<byte> XsdNamespaceUtf8 => "http://www.w3.org/2001/XMLSchema"u8;

        #endregion

        #region Public Methods

        /// <summary>
        /// Generates a deterministic UTF-8 WSDL 1.1 document with inline per-namespace <c>xs:schema</c> blocks.
        /// </summary>
        /// <param name="serviceMetadata">The validated service contract metadata.</param>
        /// <param name="addressPolicy">The explicit public endpoint address policy.</param>
        /// <returns>The UTF-8 encoded WSDL 1.1 document bytes.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="serviceMetadata"/> or <paramref name="addressPolicy"/> is <see langword="null"/>.</exception>
        public static byte[] GenerateWsdl(
            SoapServiceMetadata serviceMetadata,
            SoapMetadataAddressPolicy addressPolicy)
        {
            ArgumentNullException.ThrowIfNull(serviceMetadata);
            ArgumentNullException.ThrowIfNull(addressPolicy);

            SchemaPartitionPlan plan = BuildSchemaPlan(serviceMetadata);
            return WriteWsdlBytes(
                serviceMetadata,
                addressPolicy,
                plan,
                SoapWsdlGenerationMode.InlineSchemas,
                schemaLocations: null);
        }

        /// <summary>
        /// Generates a pre-validated immutable <see cref="SoapMetadataDocument"/> containing single-file WSDL 1.1 metadata.
        /// </summary>
        /// <param name="serviceMetadata">The validated service contract metadata.</param>
        /// <param name="addressPolicy">The explicit public endpoint address policy.</param>
        /// <returns>A pre-validated <see cref="SoapMetadataDocument"/> with SHA-256 ETag.</returns>
        public static SoapMetadataDocument GenerateWsdlDocument(
            SoapServiceMetadata serviceMetadata,
            SoapMetadataAddressPolicy addressPolicy)
        {
            byte[] wsdlBytes = GenerateWsdl(serviceMetadata, addressPolicy);
            return SoapMetadataDocument.CreateWsdl(wsdlBytes);
        }

        /// <summary>
        /// Generates a split WSDL 1.1 document and separate per-namespace XSD schema documents,
        /// returning a pre-validated immutable <see cref="SoapStaticMetadataProvider"/>.
        /// </summary>
        /// <param name="serviceMetadata">The validated service contract metadata.</param>
        /// <param name="addressPolicy">The explicit public endpoint address policy.</param>
        /// <param name="useQuerySchemaLocations">
        /// When <see langword="true"/>, emits <c>schemaLocation</c> URIs using <see cref="SoapMetadataAddressPolicy.GetSchemaLocation"/>
        /// (for example, <c>https://api.example.com/Service.svc?xsd=xsd0</c>); when <see langword="false"/>, emits local relative file names (<c>xsd0.xsd</c>).
        /// </param>
        /// <returns>An immutable <see cref="SoapStaticMetadataProvider"/> holding the WSDL and named XSD schemas.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="serviceMetadata"/> or <paramref name="addressPolicy"/> is <see langword="null"/>.</exception>
        public static SoapStaticMetadataProvider GenerateStaticProvider(
            SoapServiceMetadata serviceMetadata,
            SoapMetadataAddressPolicy addressPolicy,
            bool useQuerySchemaLocations = true)
        {
            ArgumentNullException.ThrowIfNull(serviceMetadata);
            ArgumentNullException.ThrowIfNull(addressPolicy);

            SchemaPartitionPlan plan = BuildSchemaPlan(serviceMetadata);
            Dictionary<string, string> schemaLocations = new Dictionary<string, string>(plan.OrderedNamespaces.Count, StringComparer.Ordinal);
            List<SoapMetadataDocument> schemaDocuments = new List<SoapMetadataDocument>(plan.OrderedNamespaces.Count);

            for (int index = 0; index < plan.OrderedNamespaces.Count; index++)
            {
                string ns = plan.OrderedNamespaces[index];
                string schemaName = useQuerySchemaLocations
                    ? $"xsd{index.ToString(CultureInfo.InvariantCulture)}"
                    : $"xsd{index.ToString(CultureInfo.InvariantCulture)}.xsd";

                string schemaLocation = useQuerySchemaLocations
                    ? addressPolicy.GetSchemaLocation(schemaName)
                    : schemaName;

                schemaLocations.Add(ns, schemaLocation);
            }

            for (int index = 0; index < plan.OrderedNamespaces.Count; index++)
            {
                string ns = plan.OrderedNamespaces[index];
                string schemaName = useQuerySchemaLocations
                    ? $"xsd{index.ToString(CultureInfo.InvariantCulture)}"
                    : $"xsd{index.ToString(CultureInfo.InvariantCulture)}.xsd";

                byte[] schemaBytes = WriteStandaloneSchemaBytes(plan.Buckets[ns], schemaLocations);
                schemaDocuments.Add(SoapMetadataDocument.CreateSchema(schemaName, schemaBytes));
            }

            byte[] wsdlBytes = WriteWsdlBytes(
                serviceMetadata,
                addressPolicy,
                plan,
                SoapWsdlGenerationMode.ExternalSchemas,
                schemaLocations);

            SoapMetadataDocument wsdlDocument = SoapMetadataDocument.CreateWsdl(wsdlBytes);
            return new SoapStaticMetadataProvider(wsdlDocument, schemaDocuments);
        }

        #endregion

        #region Private Methods

        private static void AddImportedNamespace(
            string sourceNamespace,
            string referencedNamespace,
            NamespaceSchemaBucket bucket)
        {
            if (string.Equals(referencedNamespace, XmlSchemaNamespace, StringComparison.Ordinal)
                || string.Equals(referencedNamespace, sourceNamespace, StringComparison.Ordinal))
            {
                return;
            }

            bucket.ImportedNamespaces.Add(referencedNamespace);
        }

        private static SchemaPartitionPlan BuildSchemaPlan(SoapServiceMetadata serviceMetadata)
        {
            Dictionary<string, NamespaceSchemaBucket> buckets = new Dictionary<string, NamespaceSchemaBucket>(StringComparer.Ordinal);
            HashSet<SoapQName> visitedTypes = new HashSet<SoapQName>();

            for (int index = 0; index < serviceMetadata.Operations.Count; index++)
            {
                SoapOperationMetadata operation = serviceMetadata.Operations[index];
                CollectGlobalElement(operation.RequestSchema, buckets, visitedTypes);
                CollectGlobalElement(operation.ResponseSchema, buckets, visitedTypes);
            }

            List<string> orderedNamespaces = new List<string>(buckets.Count);
            if (buckets.ContainsKey(serviceMetadata.TargetNamespace))
            {
                orderedNamespaces.Add(serviceMetadata.TargetNamespace);
            }

            List<string> remainingNamespaces = new List<string>(buckets.Count);
            foreach (string ns in buckets.Keys)
            {
                if (!string.Equals(ns, serviceMetadata.TargetNamespace, StringComparison.Ordinal))
                {
                    remainingNamespaces.Add(ns);
                }
            }

            remainingNamespaces.Sort(StringComparer.Ordinal);
            orderedNamespaces.AddRange(remainingNamespaces);

            Dictionary<string, string> definitionsPrefixes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [serviceMetadata.TargetNamespace] = "tns"
            };

            int prefixIndex = 0;
            for (int index = 0; index < orderedNamespaces.Count; index++)
            {
                string ns = orderedNamespaces[index];
                if (!definitionsPrefixes.ContainsKey(ns))
                {
                    definitionsPrefixes.Add(ns, $"ns{prefixIndex.ToString(CultureInfo.InvariantCulture)}");
                    prefixIndex++;
                }
            }

            foreach (NamespaceSchemaBucket bucket in buckets.Values)
            {
                bucket.GlobalElements.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
                bucket.NamedTypes.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.QName.LocalName, right.QName.LocalName));
            }

            return new SchemaPartitionPlan(buckets, orderedNamespaces, definitionsPrefixes);
        }

        private static void CollectGlobalElement(
            SoapSchemaElement element,
            Dictionary<string, NamespaceSchemaBucket> buckets,
            HashSet<SoapQName> visitedTypes)
        {
            NamespaceSchemaBucket bucket = GetOrCreateBucket(element.NamespaceUri, buckets);
            if (!bucket.SeenGlobalElementNames.Add(element.Name))
            {
                return;
            }

            bucket.GlobalElements.Add(element);

            if (element.Type is not null)
            {
                AddImportedNamespace(element.NamespaceUri, element.Type.QName.NamespaceUri, bucket);
                CollectType(element.Type, buckets, visitedTypes);
            }

            for (int index = 0; index < element.InlineSequence.Count; index++)
            {
                SoapSchemaElement member = element.InlineSequence[index];
                if (member.Type is not null)
                {
                    AddImportedNamespace(element.NamespaceUri, member.Type.QName.NamespaceUri, bucket);
                    CollectType(member.Type, buckets, visitedTypes);
                }
            }
        }

        private static void CollectType(
            SoapSchemaType schemaType,
            Dictionary<string, NamespaceSchemaBucket> buckets,
            HashSet<SoapQName> visitedTypes)
        {
            if (schemaType.Kind == SoapSchemaTypeKind.Primitive)
            {
                return;
            }

            if (!visitedTypes.Add(schemaType.QName))
            {
                return;
            }

            NamespaceSchemaBucket bucket = GetOrCreateBucket(schemaType.QName.NamespaceUri, buckets);
            bucket.NamedTypes.Add(schemaType);

            for (int index = 0; index < schemaType.SequenceMembers.Count; index++)
            {
                SoapSchemaElement member = schemaType.SequenceMembers[index];
                if (member.Type is not null)
                {
                    AddImportedNamespace(schemaType.QName.NamespaceUri, member.Type.QName.NamespaceUri, bucket);
                    CollectType(member.Type, buckets, visitedTypes);
                }
            }
        }

        private static XmlWriterOptions CreateWriterOptions(bool omitXmlDeclaration)
        {
            return new XmlWriterOptions
            {
                AttributeStyle = XmlAttributeStyle.Inline,
                EmitByteOrderMark = false,
                EmptyElementStyle = XmlEmptyElementStyle.SelfClosing,
                Indented = false,
                OmitXmlDeclaration = omitXmlDeclaration,
                Version = XmlVersion.Version10
            };
        }

        private static string FormatQualifiedTypeReference(
            SoapSchemaType schemaType,
            string currentSchemaNamespace,
            Dictionary<string, string> schemaPrefixes)
        {
            if (schemaType.Kind == SoapSchemaTypeKind.Primitive)
            {
                return $"xs:{schemaType.QName.LocalName}";
            }

            if (string.Equals(schemaType.QName.NamespaceUri, currentSchemaNamespace, StringComparison.Ordinal))
            {
                return $"tns:{schemaType.QName.LocalName}";
            }

            string prefix = schemaPrefixes[schemaType.QName.NamespaceUri];
            return $"{prefix}:{schemaType.QName.LocalName}";
        }

        private static NamespaceSchemaBucket GetOrCreateBucket(
            string targetNamespace,
            Dictionary<string, NamespaceSchemaBucket> buckets)
        {
            if (!buckets.TryGetValue(targetNamespace, out NamespaceSchemaBucket? bucket))
            {
                bucket = new NamespaceSchemaBucket(targetNamespace);
                buckets.Add(targetNamespace, bucket);
            }

            return bucket;
        }

        private static void WriteBinding(
            Utf8XmlWriter writer,
            string bindingName,
            string portTypeName,
            bool isSoap12,
            IReadOnlyList<SoapOperationMetadata> operations)
        {
            ReadOnlySpan<byte> bindingPrefix = isSoap12 ? Soap12PrefixUtf8 : Soap11PrefixUtf8;
            ReadOnlySpan<byte> bindingNs = isSoap12 ? Soap12WsdlNamespaceUtf8 : Soap11WsdlNamespaceUtf8;

            writer.WriteStartElement(WsdlPrefixUtf8, "binding"u8, WsdlNamespaceUtf8);
            writer.WriteAttributeString("name", bindingName);
            writer.WriteAttributeString("type", $"tns:{portTypeName}");

            writer.WriteStartElement(bindingPrefix, "binding"u8, bindingNs);
            writer.WriteAttributeString("style", "document");
            writer.WriteAttributeString("transport", SoapHttpTransport);
            writer.WriteEndElement();

            for (int index = 0; index < operations.Count; index++)
            {
                SoapOperationDescriptor descriptor = operations[index].Descriptor;

                writer.WriteStartElement(WsdlPrefixUtf8, "operation"u8, WsdlNamespaceUtf8);
                writer.WriteAttributeString("name", descriptor.Name);

                writer.WriteStartElement(bindingPrefix, "operation"u8, bindingNs);
                writer.WriteAttributeString("soapAction", descriptor.Action);
                writer.WriteAttributeString("style", "document");
                writer.WriteEndElement();

                writer.WriteStartElement(WsdlPrefixUtf8, "input"u8, WsdlNamespaceUtf8);
                writer.WriteStartElement(bindingPrefix, "body"u8, bindingNs);
                writer.WriteAttributeString("use", "literal");
                writer.WriteEndElement();
                writer.WriteEndElement();

                writer.WriteStartElement(WsdlPrefixUtf8, "output"u8, WsdlNamespaceUtf8);
                writer.WriteStartElement(bindingPrefix, "body"u8, bindingNs);
                writer.WriteAttributeString("use", "literal");
                writer.WriteEndElement();
                writer.WriteEndElement();

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        private static void WriteExternalImportsSchema(
            Utf8XmlWriter writer,
            SoapServiceMetadata serviceMetadata,
            SchemaPartitionPlan plan,
            Dictionary<string, string> schemaLocations)
        {
            writer.WriteStartElement(XsPrefixUtf8, "schema"u8, XsdNamespaceUtf8);
            writer.WriteAttributeString("targetNamespace", $"{serviceMetadata.TargetNamespace}/Imports");

            for (int index = 0; index < plan.OrderedNamespaces.Count; index++)
            {
                string ns = plan.OrderedNamespaces[index];
                writer.WriteStartElement(XsPrefixUtf8, "import"u8, XsdNamespaceUtf8);
                writer.WriteAttributeString("namespace", ns);
                writer.WriteAttributeString("schemaLocation", schemaLocations[ns]);
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        private static void WriteSchemaContent(
            Utf8XmlWriter writer,
            NamespaceSchemaBucket bucket,
            Dictionary<string, string>? schemaLocations)
        {
            List<string> importedNamespaces = new List<string>(bucket.ImportedNamespaces);
            importedNamespaces.Sort(StringComparer.Ordinal);

            Dictionary<string, string> localPrefixes = new Dictionary<string, string>(importedNamespaces.Count, StringComparer.Ordinal);
            for (int index = 0; index < importedNamespaces.Count; index++)
            {
                localPrefixes.Add(importedNamespaces[index], $"ns{index.ToString(CultureInfo.InvariantCulture)}");
            }

            writer.WriteStartElement(XsPrefixUtf8, "schema"u8, XsdNamespaceUtf8);
            writer.WriteAttributeString("elementFormDefault", "qualified");
            writer.WriteAttributeString("targetNamespace", bucket.TargetNamespace);
            writer.WriteNamespaceDeclaration("tns", bucket.TargetNamespace);

            for (int index = 0; index < importedNamespaces.Count; index++)
            {
                string importedNs = importedNamespaces[index];
                writer.WriteNamespaceDeclaration(localPrefixes[importedNs], importedNs);
            }

            for (int index = 0; index < importedNamespaces.Count; index++)
            {
                string importedNs = importedNamespaces[index];
                writer.WriteStartElement(XsPrefixUtf8, "import"u8, XsdNamespaceUtf8);
                writer.WriteAttributeString("namespace", importedNs);
                if (schemaLocations is not null && schemaLocations.TryGetValue(importedNs, out string? location))
                {
                    writer.WriteAttributeString("schemaLocation", location);
                }

                writer.WriteEndElement();
            }

            for (int index = 0; index < bucket.GlobalElements.Count; index++)
            {
                SoapSchemaElement globalElement = bucket.GlobalElements[index];
                WriteSchemaElement(writer, globalElement, isGlobal: true, bucket.TargetNamespace, localPrefixes);
            }

            for (int index = 0; index < bucket.NamedTypes.Count; index++)
            {
                SoapSchemaType schemaType = bucket.NamedTypes[index];
                WriteSchemaNamedType(writer, schemaType, bucket.TargetNamespace, localPrefixes);
            }

            writer.WriteEndElement();
        }

        private static void WriteSchemaElement(
            Utf8XmlWriter writer,
            SoapSchemaElement element,
            bool isGlobal,
            string currentSchemaNamespace,
            Dictionary<string, string> schemaPrefixes)
        {
            writer.WriteStartElement(XsPrefixUtf8, "element"u8, XsdNamespaceUtf8);

            if (!isGlobal)
            {
                if (element.MinOccurs != 1)
                {
                    writer.WriteAttributeString("minOccurs", element.MinOccurs.ToString(CultureInfo.InvariantCulture));
                }

                if (element.MaxOccurs is null)
                {
                    writer.WriteAttributeString("maxOccurs", "unbounded");
                }
                else if (element.MaxOccurs.Value != 1)
                {
                    writer.WriteAttributeString("maxOccurs", element.MaxOccurs.Value.ToString(CultureInfo.InvariantCulture));
                }
            }

            writer.WriteAttributeString("name", element.Name);

            if (element.IsNillable)
            {
                writer.WriteAttributeString("nillable", "true");
            }

            if (element.Type is not null)
            {
                writer.WriteAttributeString(
                    "type",
                    FormatQualifiedTypeReference(element.Type, currentSchemaNamespace, schemaPrefixes));
            }
            else
            {
                writer.WriteStartElement(XsPrefixUtf8, "complexType"u8, XsdNamespaceUtf8);
                writer.WriteStartElement(XsPrefixUtf8, "sequence"u8, XsdNamespaceUtf8);

                for (int index = 0; index < element.InlineSequence.Count; index++)
                {
                    WriteSchemaElement(
                        writer,
                        element.InlineSequence[index],
                        isGlobal: false,
                        currentSchemaNamespace,
                        schemaPrefixes);
                }

                writer.WriteEndElement();
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        private static void WriteSchemaNamedType(
            Utf8XmlWriter writer,
            SoapSchemaType schemaType,
            string currentSchemaNamespace,
            Dictionary<string, string> schemaPrefixes)
        {
            if (schemaType.Kind == SoapSchemaTypeKind.Complex)
            {
                writer.WriteStartElement(XsPrefixUtf8, "complexType"u8, XsdNamespaceUtf8);
                writer.WriteAttributeString("name", schemaType.QName.LocalName);
                writer.WriteStartElement(XsPrefixUtf8, "sequence"u8, XsdNamespaceUtf8);

                for (int index = 0; index < schemaType.SequenceMembers.Count; index++)
                {
                    WriteSchemaElement(
                        writer,
                        schemaType.SequenceMembers[index],
                        isGlobal: false,
                        currentSchemaNamespace,
                        schemaPrefixes);
                }

                writer.WriteEndElement();
                writer.WriteEndElement();
                return;
            }

            if (schemaType.Kind == SoapSchemaTypeKind.Enum)
            {
                writer.WriteStartElement(XsPrefixUtf8, "simpleType"u8, XsdNamespaceUtf8);
                writer.WriteAttributeString("name", schemaType.QName.LocalName);
                writer.WriteStartElement(XsPrefixUtf8, "restriction"u8, XsdNamespaceUtf8);
                writer.WriteAttributeString("base", "xs:string");

                for (int index = 0; index < schemaType.EnumValues.Count; index++)
                {
                    writer.WriteStartElement(XsPrefixUtf8, "enumeration"u8, XsdNamespaceUtf8);
                    writer.WriteAttributeString("value", schemaType.EnumValues[index]);
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
                writer.WriteEndElement();
            }
        }

        private static byte[] WriteStandaloneSchemaBytes(
            NamespaceSchemaBucket bucket,
            Dictionary<string, string> schemaLocations)
        {
            ArrayBufferWriter<byte> buffer = new ArrayBufferWriter<byte>(1024);
            using (Utf8XmlWriter writer = new Utf8XmlWriter(buffer, CreateWriterOptions(omitXmlDeclaration: false)))
            {
                writer.WriteXmlDeclaration();
                WriteSchemaContent(writer, bucket, schemaLocations);
                writer.Flush();
            }

            return buffer.WrittenSpan.ToArray();
        }

        private static byte[] WriteWsdlBytes(
            SoapServiceMetadata serviceMetadata,
            SoapMetadataAddressPolicy addressPolicy,
            SchemaPartitionPlan plan,
            SoapWsdlGenerationMode mode,
            Dictionary<string, string>? schemaLocations)
        {
            ArrayBufferWriter<byte> buffer = new ArrayBufferWriter<byte>(2048);
            bool hasSoap11 = (serviceMetadata.SupportedVersions & SoapVersionFlags.Soap11) != 0;
            bool hasSoap12 = (serviceMetadata.SupportedVersions & SoapVersionFlags.Soap12) != 0;

            using (Utf8XmlWriter writer = new Utf8XmlWriter(buffer, CreateWriterOptions(omitXmlDeclaration: false)))
            {
                writer.WriteXmlDeclaration();

                writer.WriteStartElement(WsdlPrefixUtf8, "definitions"u8, WsdlNamespaceUtf8);
                writer.WriteAttributeString("name", serviceMetadata.ServiceName);
                writer.WriteAttributeString("targetNamespace", serviceMetadata.TargetNamespace);
                writer.WriteNamespaceDeclaration("xsd", XmlSchemaNamespace);
                writer.WriteNamespaceDeclaration("tns", serviceMetadata.TargetNamespace);

                if (hasSoap11)
                {
                    writer.WriteNamespaceDeclaration(Soap11PrefixUtf8, Soap11WsdlNamespaceUtf8);
                }

                if (hasSoap12)
                {
                    writer.WriteNamespaceDeclaration(Soap12PrefixUtf8, Soap12WsdlNamespaceUtf8);
                }

                for (int index = 0; index < plan.OrderedNamespaces.Count; index++)
                {
                    string ns = plan.OrderedNamespaces[index];
                    string prefix = plan.DefinitionsPrefixes[ns];
                    if (!string.Equals(prefix, "tns", StringComparison.Ordinal))
                    {
                        writer.WriteNamespaceDeclaration(prefix, ns);
                    }
                }

                writer.WriteStartElement(WsdlPrefixUtf8, "types"u8, WsdlNamespaceUtf8);
                if (mode == SoapWsdlGenerationMode.InlineSchemas)
                {
                    for (int index = 0; index < plan.OrderedNamespaces.Count; index++)
                    {
                        string ns = plan.OrderedNamespaces[index];
                        WriteSchemaContent(writer, plan.Buckets[ns], schemaLocations: null);
                    }
                }
                else
                {
                    WriteExternalImportsSchema(writer, serviceMetadata, plan, schemaLocations!);
                }

                writer.WriteEndElement();

                for (int index = 0; index < serviceMetadata.Operations.Count; index++)
                {
                    SoapOperationDescriptor descriptor = serviceMetadata.Operations[index].Descriptor;
                    string inputMessageName = $"{serviceMetadata.PortTypeName}_{descriptor.Name}_InputMessage";
                    string outputMessageName = $"{serviceMetadata.PortTypeName}_{descriptor.Name}_OutputMessage";

                    string requestPrefix = plan.DefinitionsPrefixes[descriptor.RequestElement.NamespaceUri];
                    string responsePrefix = plan.DefinitionsPrefixes[descriptor.ResponseElement.NamespaceUri];

                    writer.WriteStartElement(WsdlPrefixUtf8, "message"u8, WsdlNamespaceUtf8);
                    writer.WriteAttributeString("name", inputMessageName);
                    writer.WriteStartElement(WsdlPrefixUtf8, "part"u8, WsdlNamespaceUtf8);
                    writer.WriteAttributeString("name", "parameters");
                    writer.WriteAttributeString("element", $"{requestPrefix}:{descriptor.RequestElement.LocalName}");
                    writer.WriteEndElement();
                    writer.WriteEndElement();

                    writer.WriteStartElement(WsdlPrefixUtf8, "message"u8, WsdlNamespaceUtf8);
                    writer.WriteAttributeString("name", outputMessageName);
                    writer.WriteStartElement(WsdlPrefixUtf8, "part"u8, WsdlNamespaceUtf8);
                    writer.WriteAttributeString("name", "parameters");
                    writer.WriteAttributeString("element", $"{responsePrefix}:{descriptor.ResponseElement.LocalName}");
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                }

                writer.WriteStartElement(WsdlPrefixUtf8, "portType"u8, WsdlNamespaceUtf8);
                writer.WriteAttributeString("name", serviceMetadata.PortTypeName);

                for (int index = 0; index < serviceMetadata.Operations.Count; index++)
                {
                    SoapOperationDescriptor descriptor = serviceMetadata.Operations[index].Descriptor;
                    string inputMessageName = $"tns:{serviceMetadata.PortTypeName}_{descriptor.Name}_InputMessage";
                    string outputMessageName = $"tns:{serviceMetadata.PortTypeName}_{descriptor.Name}_OutputMessage";

                    writer.WriteStartElement(WsdlPrefixUtf8, "operation"u8, WsdlNamespaceUtf8);
                    writer.WriteAttributeString("name", descriptor.Name);

                    writer.WriteStartElement(WsdlPrefixUtf8, "input"u8, WsdlNamespaceUtf8);
                    writer.WriteAttributeString("message", inputMessageName);
                    writer.WriteEndElement();

                    writer.WriteStartElement(WsdlPrefixUtf8, "output"u8, WsdlNamespaceUtf8);
                    writer.WriteAttributeString("message", outputMessageName);
                    writer.WriteEndElement();

                    writer.WriteEndElement();
                }

                writer.WriteEndElement();

                string soap11BindingName = $"BasicHttpBinding_{serviceMetadata.PortTypeName}";
                string soap12BindingName = $"Soap12HttpBinding_{serviceMetadata.PortTypeName}";

                if (hasSoap11)
                {
                    WriteBinding(writer, soap11BindingName, serviceMetadata.PortTypeName, isSoap12: false, serviceMetadata.Operations);
                }

                if (hasSoap12)
                {
                    WriteBinding(writer, soap12BindingName, serviceMetadata.PortTypeName, isSoap12: true, serviceMetadata.Operations);
                }

                string endpointAddress = addressPolicy.GetEndpointAddress();
                writer.WriteStartElement(WsdlPrefixUtf8, "service"u8, WsdlNamespaceUtf8);
                writer.WriteAttributeString("name", serviceMetadata.ServiceName);

                if (hasSoap11)
                {
                    writer.WriteStartElement(WsdlPrefixUtf8, "port"u8, WsdlNamespaceUtf8);
                    writer.WriteAttributeString("name", soap11BindingName);
                    writer.WriteAttributeString("binding", $"tns:{soap11BindingName}");

                    writer.WriteStartElement(Soap11PrefixUtf8, "address"u8, Soap11WsdlNamespaceUtf8);
                    writer.WriteAttributeString("location", endpointAddress);
                    writer.WriteEndElement();

                    writer.WriteEndElement();
                }

                if (hasSoap12)
                {
                    writer.WriteStartElement(WsdlPrefixUtf8, "port"u8, WsdlNamespaceUtf8);
                    writer.WriteAttributeString("name", soap12BindingName);
                    writer.WriteAttributeString("binding", $"tns:{soap12BindingName}");

                    writer.WriteStartElement(Soap12PrefixUtf8, "address"u8, Soap12WsdlNamespaceUtf8);
                    writer.WriteAttributeString("location", endpointAddress);
                    writer.WriteEndElement();

                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
                writer.WriteEndElement();
                writer.Flush();
            }

            return buffer.WrittenSpan.ToArray();
        }

        #endregion

        #region Nested Types

        private sealed class NamespaceSchemaBucket
        {

            #region Properties

            public List<SoapSchemaElement> GlobalElements { get; } = [];

            public HashSet<string> ImportedNamespaces { get; } = new HashSet<string>(StringComparer.Ordinal);

            public List<SoapSchemaType> NamedTypes { get; } = [];

            public HashSet<string> SeenGlobalElementNames { get; } = new HashSet<string>(StringComparer.Ordinal);

            public string TargetNamespace { get; }

            #endregion

            #region Constructors

            public NamespaceSchemaBucket(string targetNamespace)
            {
                TargetNamespace = targetNamespace;
            }

            #endregion

        }

        private sealed class SchemaPartitionPlan
        {

            #region Properties

            public Dictionary<string, NamespaceSchemaBucket> Buckets { get; }

            public Dictionary<string, string> DefinitionsPrefixes { get; }

            public List<string> OrderedNamespaces { get; }

            #endregion

            #region Constructors

            public SchemaPartitionPlan(
                Dictionary<string, NamespaceSchemaBucket> buckets,
                List<string> orderedNamespaces,
                Dictionary<string, string> definitionsPrefixes)
            {
                Buckets = buckets;
                OrderedNamespaces = orderedNamespaces;
                DefinitionsPrefixes = definitionsPrefixes;
            }

            #endregion

        }

        #endregion

    }

}
