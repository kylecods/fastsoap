using System;
using System.Buffers;
using System.Text;

namespace FastSoap.Abstractions
{

    /// <summary>
    /// Represents an immutable, validated XML expanded qualified name (namespace URI and local name)
    /// with pre-encoded UTF-8 byte spans for zero-allocation ordinal matching.
    /// </summary>
    /// <example>
    /// <code>
    /// SoapQName operationElement = new SoapQName("urn:payments", "Transfer");
    /// </code>
    /// </example>
    public readonly record struct SoapQName
    {

        #region Fields

        private static readonly SearchValues<char> ForbiddenLocalNameChars = SearchValues.Create("\0\t\n\r :<>\"'&/\\");
        private static readonly SearchValues<char> ForbiddenNamespaceChars = SearchValues.Create("\0\r\n");

        private readonly byte[] _localNameUtf8;
        private readonly byte[] _namespaceUriUtf8;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the XML local name (NCName) without prefix or namespace.
        /// </summary>
        public string LocalName { get; }

        /// <summary>
        /// Gets the pre-encoded UTF-8 bytes of <see cref="LocalName"/>.
        /// </summary>
        public ReadOnlySpan<byte> LocalNameUtf8 => _localNameUtf8;

        /// <summary>
        /// Gets the XML namespace URI, or <see cref="string.Empty"/> for an unqualified name.
        /// </summary>
        public string NamespaceUri { get; }

        /// <summary>
        /// Gets the pre-encoded UTF-8 bytes of <see cref="NamespaceUri"/>.
        /// </summary>
        public ReadOnlySpan<byte> NamespaceUriUtf8 => _namespaceUriUtf8;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new validated <see cref="SoapQName"/> instance and pre-encodes its UTF-8 tokens.
        /// </summary>
        /// <param name="namespaceUri">The XML namespace URI, or <see cref="string.Empty"/> when unqualified.</param>
        /// <param name="localName">The XML local name (must be a non-empty valid NCName without colons or whitespace).</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="namespaceUri"/> or <paramref name="localName"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="localName"/> is empty, whitespace, or contains invalid NCName characters, or when <paramref name="namespaceUri"/> contains control characters.</exception>
        public SoapQName(string namespaceUri, string localName)
        {
            ArgumentNullException.ThrowIfNull(namespaceUri);
            ArgumentException.ThrowIfNullOrWhiteSpace(localName);

            ValidateNamespaceUri(namespaceUri);
            ValidateLocalName(localName);

            NamespaceUri = namespaceUri;
            LocalName = localName;
            _namespaceUriUtf8 = namespaceUri.Length == 0 ? [] : Encoding.UTF8.GetBytes(namespaceUri);
            _localNameUtf8 = Encoding.UTF8.GetBytes(localName);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Deconstructs the qualified name into its namespace URI and local name.
        /// </summary>
        /// <param name="namespaceUri">Receives <see cref="NamespaceUri"/>.</param>
        /// <param name="localName">Receives <see cref="LocalName"/>.</param>
        public void Deconstruct(out string namespaceUri, out string localName)
        {
            namespaceUri = NamespaceUri;
            localName = LocalName;
        }

        /// <summary>
        /// Determines whether this instance equals another <see cref="SoapQName"/> using ordinal comparison.
        /// </summary>
        /// <param name="other">The other qualified name to compare.</param>
        /// <returns><see langword="true"/> if both namespace URI and local name match ordinally; otherwise <see langword="false"/>.</returns>
        public bool Equals(SoapQName other)
        {
            return string.Equals(NamespaceUri, other.NamespaceUri, StringComparison.Ordinal)
                && string.Equals(LocalName, other.LocalName, StringComparison.Ordinal);
        }

        /// <summary>
        /// Returns an ordinal hash code for the qualified name.
        /// </summary>
        /// <returns>The combined ordinal hash code.</returns>
        public override int GetHashCode()
        {
            return HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(NamespaceUri ?? string.Empty),
                StringComparer.Ordinal.GetHashCode(LocalName ?? string.Empty));
        }

        /// <summary>
        /// Compares pre-encoded UTF-8 namespace URI and local name spans against raw UTF-8 wire tokens.
        /// </summary>
        /// <param name="namespaceUriUtf8">The candidate UTF-8 namespace URI bytes.</param>
        /// <param name="localNameUtf8">The candidate UTF-8 local name bytes.</param>
        /// <returns><see langword="true"/> when both UTF-8 spans match byte-for-byte; otherwise <see langword="false"/>.</returns>
        public bool Matches(ReadOnlySpan<byte> namespaceUriUtf8, ReadOnlySpan<byte> localNameUtf8)
        {
            return LocalNameUtf8.SequenceEqual(localNameUtf8)
                && NamespaceUriUtf8.SequenceEqual(namespaceUriUtf8);
        }

        /// <summary>
        /// Formats the qualified name in Clark notation (<c>{namespace}localName</c> or <c>localName</c>).
        /// </summary>
        /// <returns>The formatted expanded qualified name.</returns>
        public override string ToString()
        {
            if (string.IsNullOrEmpty(NamespaceUri))
            {
                return LocalName ?? string.Empty;
            }

            return $"{{{NamespaceUri}}}{LocalName}";
        }

        #endregion

        #region Private Methods

        private static void ValidateLocalName(string localName)
        {
            if (localName.AsSpan().ContainsAny(ForbiddenLocalNameChars))
            {
                throw new ArgumentException(
                    "XML local name must be a valid NCName without prefixes, whitespace, or delimiter characters.",
                    nameof(localName));
            }

            char first = localName[0];
            if (char.IsDigit(first) || first == '-' || first == '.' || char.IsWhiteSpace(first))
            {
                throw new ArgumentException(
                    "XML local name cannot start with a digit, hyphen, period, or whitespace character.",
                    nameof(localName));
            }

            for (int index = 0; index < localName.Length; index++)
            {
                char current = localName[index];
                if (char.IsControl(current) || char.IsWhiteSpace(current))
                {
                    throw new ArgumentException(
                        "XML local name cannot contain control or whitespace characters.",
                        nameof(localName));
                }
            }
        }

        private static void ValidateNamespaceUri(string namespaceUri)
        {
            if (namespaceUri.AsSpan().ContainsAny(ForbiddenNamespaceChars))
            {
                throw new ArgumentException(
                    "XML namespace URI cannot contain null or newline characters.",
                    nameof(namespaceUri));
            }
        }

        #endregion

    }

}
