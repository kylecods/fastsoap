using System;
using System.Security.Cryptography;
using FastSoap.Metadata.Internal;

namespace FastSoap.Metadata
{

    /// <summary>
    /// Represents an immutable, pre-validated UTF-8 WSDL 1.1 or XSD schema artifact with a precomputed SHA-256 ETag.
    /// </summary>
    /// <example>
    /// <code>
    /// SoapMetadataDocument schema = SoapMetadataDocument.CreateSchema("xsd0", schemaUtf8Bytes);
    /// string etag = schema.ETag;
    /// </code>
    /// </example>
    /// <remarks>
    /// Byte buffers passed on creation are defensively copied so served metadata remains immutable across requests.
    /// </remarks>
    public sealed class SoapMetadataDocument
    {

        #region Fields

        /// <summary>
        /// Canonical HTTP <c>Content-Type</c> header value for served WSDL and XSD metadata artifacts.
        /// </summary>
        public const string XmlContentType = "text/xml; charset=utf-8";

        private readonly byte[] _contentUtf8;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the HTTP <c>Content-Type</c> value (<c>text/xml; charset=utf-8</c>).
        /// </summary>
        public string ContentType => XmlContentType;

        /// <summary>
        /// Gets the immutable UTF-8 encoded XML document bytes.
        /// </summary>
        public ReadOnlyMemory<byte> ContentUtf8 => _contentUtf8;

        /// <summary>
        /// Gets the precomputed quoted strong SHA-256 HTTP <c>ETag</c> value.
        /// </summary>
        public string ETag { get; }

        /// <summary>
        /// Gets the metadata artifact kind (<see cref="SoapMetadataDocumentKind.Wsdl"/> or <see cref="SoapMetadataDocumentKind.XmlSchema"/>).
        /// </summary>
        public SoapMetadataDocumentKind Kind { get; }

        /// <summary>
        /// Gets the normalized artifact name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the lowercase hexadecimal SHA-256 digest of <see cref="ContentUtf8"/>.
        /// </summary>
        public string Sha256Hex { get; }

        #endregion

        #region Constructors

        private SoapMetadataDocument(string name, SoapMetadataDocumentKind kind, byte[] ownedUtf8Content)
        {
            Name = name;
            Kind = kind;
            _contentUtf8 = ownedUtf8Content;

            Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
            SHA256.HashData(ownedUtf8Content, hash);
            Sha256Hex = Convert.ToHexStringLower(hash);
            ETag = $"\"{Sha256Hex}\"";
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Creates a pre-validated immutable XSD schema <see cref="SoapMetadataDocument"/>.
        /// </summary>
        /// <param name="name">The normalized schema name (for example, <c>xsd0</c> or <c>payments.xsd</c>).</param>
        /// <param name="utf8Content">The UTF-8 encoded XSD document bytes.</param>
        /// <returns>A validated immutable <see cref="SoapMetadataDocument"/>.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> fails strict normalization or <paramref name="utf8Content"/> is not valid UTF-8 XSD XML 1.0.</exception>
        public static SoapMetadataDocument CreateSchema(string name, ReadOnlySpan<byte> utf8Content)
        {
            if (!SoapMetadataPathNormalizer.TryNormalizeSchemaName(name, out string? normalizedName))
            {
                throw new ArgumentException("Schema name contains invalid, traversal, query, or URL characters.", nameof(name));
            }

            SoapMetadataXmlValidator.ValidateDocument(utf8Content, SoapMetadataDocumentKind.XmlSchema, nameof(utf8Content));

            return new SoapMetadataDocument(normalizedName, SoapMetadataDocumentKind.XmlSchema, utf8Content.ToArray());
        }

        /// <summary>
        /// Creates a pre-validated immutable WSDL 1.1 <see cref="SoapMetadataDocument"/>.
        /// </summary>
        /// <param name="name">The normalized WSDL artifact name (defaults to <c>wsdl</c> when using <see cref="CreateWsdl(ReadOnlySpan{byte})"/>).</param>
        /// <param name="utf8Content">The UTF-8 encoded WSDL 1.1 document bytes.</param>
        /// <returns>A validated immutable <see cref="SoapMetadataDocument"/>.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> fails strict normalization or <paramref name="utf8Content"/> is not valid UTF-8 WSDL 1.1 XML 1.0.</exception>
        public static SoapMetadataDocument CreateWsdl(string name, ReadOnlySpan<byte> utf8Content)
        {
            if (!SoapMetadataPathNormalizer.TryNormalizeSchemaName(name, out string? normalizedName))
            {
                throw new ArgumentException("WSDL artifact name contains invalid, traversal, query, or URL characters.", nameof(name));
            }

            SoapMetadataXmlValidator.ValidateDocument(utf8Content, SoapMetadataDocumentKind.Wsdl, nameof(utf8Content));

            return new SoapMetadataDocument(normalizedName, SoapMetadataDocumentKind.Wsdl, utf8Content.ToArray());
        }

        /// <summary>
        /// Creates a pre-validated immutable WSDL 1.1 <see cref="SoapMetadataDocument"/> named <c>wsdl</c>.
        /// </summary>
        /// <param name="utf8Content">The UTF-8 encoded WSDL 1.1 document bytes.</param>
        /// <returns>A validated immutable <see cref="SoapMetadataDocument"/>.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="utf8Content"/> is not valid UTF-8 WSDL 1.1 XML 1.0.</exception>
        public static SoapMetadataDocument CreateWsdl(ReadOnlySpan<byte> utf8Content)
        {
            return CreateWsdl("wsdl", utf8Content);
        }

        /// <summary>
        /// Determines whether an HTTP <c>If-None-Match</c> header value matches this document's strong <see cref="ETag"/>.
        /// </summary>
        /// <param name="ifNoneMatchHeader">The raw <c>If-None-Match</c> header value.</param>
        /// <returns><see langword="true"/> if <paramref name="ifNoneMatchHeader"/> equals <c>*</c> or contains an exact strong ordinal match for <see cref="ETag"/>; otherwise <see langword="false"/>.</returns>
        public bool MatchesETag(ReadOnlySpan<char> ifNoneMatchHeader)
        {
            ReadOnlySpan<char> trimmed = ifNoneMatchHeader.Trim();
            if (trimmed.IsEmpty)
            {
                return false;
            }

            if (trimmed.SequenceEqual("*"))
            {
                return true;
            }

            foreach (Range range in trimmed.Split(','))
            {
                ReadOnlySpan<char> candidate = trimmed[range].Trim();
                if (candidate.SequenceEqual(ETag))
                {
                    return true;
                }
            }

            return false;
        }

        #endregion

    }

}
