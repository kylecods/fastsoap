using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using FastSoap.Metadata.Internal;

namespace FastSoap.Metadata
{

    /// <summary>
    /// Stores and serves immutable, pre-validated UTF-8 WSDL 1.1 and named XSD schema artifacts
    /// with strict path normalization and zero runtime XML parsing or network resolution.
    /// </summary>
    /// <example>
    /// <code>
    /// SoapStaticMetadataProvider provider = SoapStaticMetadataProvider.Create(
    ///     wsdlUtf8,
    ///     [new KeyValuePair&lt;string, byte[]&gt;("xsd0", xsd0Utf8)]);
    ///
    /// if (provider.TryGetSchema("xsd0", out SoapMetadataDocument? schema))
    /// {
    ///     // Serve schema.ContentUtf8 with schema.ContentType and schema.ETag.
    /// }
    /// </code>
    /// </example>
    /// <remarks>
    /// Lookup keys are strictly normalized with <see cref="StringComparer.Ordinal"/> and reject path traversal,
    /// query injection, percent-encoding, remote URLs, and unknown schema names.
    /// </remarks>
    public sealed class SoapStaticMetadataProvider
    {

        #region Fields

        private readonly FrozenDictionary<string, SoapMetadataDocument> _schemas;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the number of registered named XSD schema documents.
        /// </summary>
        public int SchemaCount => _schemas.Count;

        /// <summary>
        /// Gets the sorted collection of registered normalized schema names.
        /// </summary>
        public IReadOnlyList<string> SchemaNames { get; }

        /// <summary>
        /// Gets the primary immutable WSDL 1.1 metadata document.
        /// </summary>
        public SoapMetadataDocument WsdlDocument { get; }

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new <see cref="SoapStaticMetadataProvider"/> with a pre-validated WSDL 1.1 document
        /// and optional named XSD schema documents.
        /// </summary>
        /// <param name="wsdlDocument">The primary WSDL 1.1 document.</param>
        /// <param name="schemas">Optional named XSD schema documents.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="wsdlDocument"/> is <see langword="null"/> or any item in <paramref name="schemas"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="wsdlDocument"/> is not WSDL, any schema is not XSD, or duplicate schema names exist.</exception>
        public SoapStaticMetadataProvider(
            SoapMetadataDocument wsdlDocument,
            IEnumerable<SoapMetadataDocument>? schemas = null)
        {
            ArgumentNullException.ThrowIfNull(wsdlDocument);

            if (wsdlDocument.Kind != SoapMetadataDocumentKind.Wsdl)
            {
                throw new ArgumentException("Primary metadata document must be of kind Wsdl.", nameof(wsdlDocument));
            }

            WsdlDocument = wsdlDocument;

            Dictionary<string, SoapMetadataDocument> schemaMap = new Dictionary<string, SoapMetadataDocument>(StringComparer.Ordinal);
            if (schemas is not null)
            {
                foreach (SoapMetadataDocument schema in schemas)
                {
                    ArgumentNullException.ThrowIfNull(schema);

                    if (schema.Kind != SoapMetadataDocumentKind.XmlSchema)
                    {
                        throw new ArgumentException($"Schema '{schema.Name}' must be of kind XmlSchema.", nameof(schemas));
                    }

                    if (!schemaMap.TryAdd(schema.Name, schema))
                    {
                        throw new ArgumentException($"Duplicate schema name '{schema.Name}'.", nameof(schemas));
                    }
                }
            }

            _schemas = schemaMap.ToFrozenDictionary(StringComparer.Ordinal);

            string[] sortedNames = new string[schemaMap.Count];
            schemaMap.Keys.CopyTo(sortedNames, 0);
            Array.Sort(sortedNames, StringComparer.Ordinal);
            SchemaNames = sortedNames;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Creates a <see cref="SoapStaticMetadataProvider"/> from raw UTF-8 WSDL 1.1 bytes and optional named XSD byte arrays.
        /// </summary>
        /// <param name="wsdlUtf8">The raw UTF-8 WSDL 1.1 bytes.</param>
        /// <param name="schemas">Optional named XSD byte payloads.</param>
        /// <returns>An immutable <see cref="SoapStaticMetadataProvider"/> containing pre-validated documents.</returns>
        public static SoapStaticMetadataProvider Create(
            ReadOnlySpan<byte> wsdlUtf8,
            IEnumerable<KeyValuePair<string, byte[]>>? schemas = null)
        {
            SoapMetadataDocument wsdlDocument = SoapMetadataDocument.CreateWsdl(wsdlUtf8);

            if (schemas is null)
            {
                return new SoapStaticMetadataProvider(wsdlDocument);
            }

            List<SoapMetadataDocument> validatedSchemas = [];
            foreach (KeyValuePair<string, byte[]> entry in schemas)
            {
                ArgumentNullException.ThrowIfNull(entry.Value);
                validatedSchemas.Add(SoapMetadataDocument.CreateSchema(entry.Key, entry.Value));
            }

            return new SoapStaticMetadataProvider(wsdlDocument, validatedSchemas);
        }

        /// <summary>
        /// Generates deterministic WSDL 1.1 and separate XSD schema documents from an explicit <see cref="SoapServiceMetadata"/>
        /// contract and registers them into a <see cref="SoapStaticMetadataProvider"/>.
        /// </summary>
        /// <param name="serviceMetadata">The validated service metadata contract.</param>
        /// <param name="addressPolicy">The explicit public endpoint address policy.</param>
        /// <returns>An immutable <see cref="SoapStaticMetadataProvider"/> populated with generated WSDL and XSD documents.</returns>
        public static SoapStaticMetadataProvider FromServiceMetadata(
            SoapServiceMetadata serviceMetadata,
            SoapMetadataAddressPolicy addressPolicy)
        {
            ArgumentNullException.ThrowIfNull(serviceMetadata);
            ArgumentNullException.ThrowIfNull(addressPolicy);

            return SoapWsdlGenerator.GenerateStaticProvider(serviceMetadata, addressPolicy);
        }

        /// <summary>
        /// Returns the primary immutable WSDL 1.1 metadata document.
        /// </summary>
        /// <returns>The primary <see cref="SoapMetadataDocument"/>.</returns>
        public SoapMetadataDocument GetWsdl()
        {
            return WsdlDocument;
        }

        /// <summary>
        /// Attempts to look up a registered XSD schema document after strict path normalization.
        /// </summary>
        /// <param name="schemaName">The untrusted schema key or relative path.</param>
        /// <param name="document">Receives the matched immutable <see cref="SoapMetadataDocument"/> when found.</param>
        /// <returns><see langword="true"/> when <paramref name="schemaName"/> is strictly valid and registered; otherwise <see langword="false"/>.</returns>
        public bool TryGetSchema(string? schemaName, [NotNullWhen(true)] out SoapMetadataDocument? document)
        {
            if (!SoapMetadataPathNormalizer.TryNormalizeSchemaName(schemaName, out string? normalizedName))
            {
                document = null;
                return false;
            }

            return _schemas.TryGetValue(normalizedName, out document);
        }

        /// <summary>
        /// Validates and normalizes a schema name, rejecting path traversal, query injection, percent-encoding, and remote URLs.
        /// </summary>
        /// <param name="schemaName">The candidate schema name.</param>
        /// <param name="normalizedName">Receives the validated normalized schema name when valid.</param>
        /// <returns><see langword="true"/> when the schema name is safe and valid; otherwise <see langword="false"/>.</returns>
        public static bool TryNormalizeSchemaName(string? schemaName, [NotNullWhen(true)] out string? normalizedName)
        {
            return SoapMetadataPathNormalizer.TryNormalizeSchemaName(schemaName, out normalizedName);
        }

        #endregion

    }

}
