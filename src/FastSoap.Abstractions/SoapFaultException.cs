using System;
using System.Buffers;
using System.Net;
using System.Text.Unicode;

namespace FastSoap.Abstractions
{

    /// <summary>
    /// Represents an explicit, structured SOAP fault thrown by a service or header handler carrying
    /// a safe public reason, optional custom fault code, optional actor/node, and validated UTF-8 XML detail payload.
    /// </summary>
    /// <example>
    /// <code>
    /// throw new SoapFaultException(
    ///     SoapFaultKind.Sender,
    ///     "Payment validation failed.",
    ///     detailXmlUtf8);
    /// </code>
    /// </example>
    /// <remarks>
    /// Unlike unhandled exceptions, <see cref="SoapFaultException"/> preserves <see cref="DetailXmlUtf8"/>,
    /// custom fault codes, and <see cref="PublicReason"/> on the wire as SOAP 1.1 <c>&lt;detail&gt;</c> or
    /// SOAP 1.2 <c>&lt;s:Detail&gt;</c>. Callers must ensure <see cref="PublicReason"/> and
    /// <see cref="DetailXmlUtf8"/> contain only safe, intentionally exposed fault data.
    /// </remarks>
    public sealed class SoapFaultException : Exception
    {

        #region Fields

        private static readonly SearchValues<byte> s_invalidXml10ControlBytes = SearchValues.Create(
        [
            0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
            0x0B, 0x0C,
            0x0E, 0x0F, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
            0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F
        ]);

        #endregion

        #region Properties

        /// <summary>
        /// Gets the optional SOAP 1.1 <c>faultactor</c> or SOAP 1.2 <c>Node</c> URI.
        /// </summary>
        public string? ActorOrNode { get; }

        /// <summary>
        /// Gets the optional custom XML fault code local name, or <see langword="null"/> to use the standard code for <see cref="FaultKind"/>.
        /// </summary>
        public string? CodeLocalName { get; }

        /// <summary>
        /// Gets the optional custom XML fault code namespace URI, or <see langword="null"/> to use the standard code for <see cref="FaultKind"/>.
        /// </summary>
        public string? CodeNamespace { get; }

        /// <summary>
        /// Gets the owned, validated UTF-8 XML fragment bytes for the SOAP fault <c>detail</c>/<c>Detail</c> element.
        /// </summary>
        public ReadOnlyMemory<byte> DetailXmlUtf8 { get; }

        /// <summary>
        /// Gets the normalized SOAP fault classification.
        /// </summary>
        public SoapFaultKind FaultKind { get; }

        /// <summary>
        /// Gets the optional explicit HTTP error status code (400-599) to emit, or <see langword="null"/> to use
        /// the default SOAP 1.1/1.2 status mapping for <see cref="FaultKind"/>.
        /// </summary>
        public HttpStatusCode? HttpStatusCode { get; }

        /// <summary>
        /// Gets the safe, public human-readable fault reason emitted on the wire.
        /// </summary>
        public string PublicReason { get; }

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new validated <see cref="SoapFaultException"/> instance.
        /// </summary>
        /// <param name="faultKind">Normalized SOAP fault classification.</param>
        /// <param name="publicReason">Safe public fault reason string.</param>
        /// <param name="detailXmlUtf8">Optional safe UTF-8 XML fragment bytes for <c>detail</c>/<c>Detail</c>.</param>
        /// <param name="codeNamespace">Optional custom fault code XML namespace URI (must be supplied together with <paramref name="codeLocalName"/>).</param>
        /// <param name="codeLocalName">Optional custom fault code XML local name (must be supplied together with <paramref name="codeNamespace"/>).</param>
        /// <param name="actorOrNode">Optional SOAP 1.1 <c>faultactor</c> or SOAP 1.2 <c>Node</c> URI.</param>
        /// <param name="innerException">Optional underlying exception for server-side diagnostics.</param>
        /// <param name="httpStatusCode">Optional HTTP error status code override (400-599), e.g., <see cref="System.Net.HttpStatusCode.InternalServerError"/> for WCF SOAP 1.2 client compatibility.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="faultKind"/> or <paramref name="httpStatusCode"/> is invalid.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="publicReason"/>, custom code arguments, <paramref name="actorOrNode"/>, or <paramref name="detailXmlUtf8"/> are invalid.</exception>
        public SoapFaultException(
            SoapFaultKind faultKind,
            string publicReason,
            ReadOnlyMemory<byte> detailXmlUtf8 = default,
            string? codeNamespace = null,
            string? codeLocalName = null,
            string? actorOrNode = null,
            Exception? innerException = null,
            HttpStatusCode? httpStatusCode = null)
            : base(ValidatePublicReason(publicReason), innerException)
        {
            if (!Enum.IsDefined(faultKind))
            {
                throw new ArgumentOutOfRangeException(nameof(faultKind), faultKind, "Unsupported SOAP fault kind.");
            }

            if (httpStatusCode is HttpStatusCode status && ((int)status < 400 || (int)status > 599))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(httpStatusCode),
                    httpStatusCode,
                    "HTTP status code for a SOAP fault must be between 400 and 599.");
            }

            if ((codeNamespace is null) != (codeLocalName is null))
            {
                throw new ArgumentException(
                    "Custom fault code namespace and local name must both be specified or both be null.",
                    codeNamespace is null ? nameof(codeNamespace) : nameof(codeLocalName));
            }

            if (codeNamespace is not null)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(codeNamespace);
                ArgumentException.ThrowIfNullOrWhiteSpace(codeLocalName);
            }

            if (actorOrNode is not null)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(actorOrNode);
            }

            ReadOnlyMemory<byte> ownedDetail = ReadOnlyMemory<byte>.Empty;
            if (!detailXmlUtf8.IsEmpty)
            {
                ValidateDetailXmlFragment(detailXmlUtf8.Span);
                ownedDetail = detailXmlUtf8.ToArray();
            }

            FaultKind = faultKind;
            HttpStatusCode = httpStatusCode;
            PublicReason = publicReason;
            DetailXmlUtf8 = ownedDetail;
            CodeNamespace = codeNamespace;
            CodeLocalName = codeLocalName;
            ActorOrNode = actorOrNode;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Converts this exception into a protocol-normalized <see cref="SoapFault"/> for <paramref name="version"/>.
        /// </summary>
        /// <param name="version">The target SOAP protocol version.</param>
        /// <param name="correlationId">Optional correlation identifier.</param>
        /// <returns>A validated <see cref="SoapFault"/> carrying this exception's fault metadata and detail XML.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="version"/> is undefined.</exception>
        public SoapFault ToSoapFault(SoapVersion version, string? correlationId = null)
        {
            if (CodeNamespace is not null && CodeLocalName is not null)
            {
                return new SoapFault(
                    version,
                    FaultKind,
                    CodeNamespace,
                    CodeLocalName,
                    PublicReason,
                    ActorOrNode,
                    DetailXmlUtf8,
                    correlationId);
            }

            return SoapFault.CreateStandard(
                version,
                FaultKind,
                PublicReason,
                ActorOrNode,
                DetailXmlUtf8,
                correlationId);
        }

        #endregion

        #region Private Methods

        private static bool ContainsAsciiCaseInsensitivePrefix(ReadOnlySpan<byte> xml, ReadOnlySpan<byte> lowerPrefix)
        {
            int searchOffset = 0;
            while (searchOffset <= xml.Length - lowerPrefix.Length)
            {
                int found = xml[searchOffset..].IndexOf("<!"u8);
                if (found < 0)
                {
                    return false;
                }

                int candidateIndex = searchOffset + found;
                if (candidateIndex + lowerPrefix.Length <= xml.Length)
                {
                    bool matches = true;
                    for (int index = 2; index < lowerPrefix.Length; index++)
                    {
                        byte b = xml[candidateIndex + index];
                        byte lower = b is >= (byte)'A' and <= (byte)'Z'
                            ? (byte)(b + 32)
                            : b;

                        if (lower != lowerPrefix[index])
                        {
                            matches = false;
                            break;
                        }
                    }

                    if (matches)
                    {
                        return true;
                    }
                }

                searchOffset = candidateIndex + 2;
            }

            return false;
        }

        private static ReadOnlySpan<byte> TrimXmlWhitespace(ReadOnlySpan<byte> span)
        {
            int start = 0;
            while (start < span.Length && IsXmlWhitespace(span[start]))
            {
                start++;
            }

            int end = span.Length;
            while (end > start && IsXmlWhitespace(span[end - 1]))
            {
                end--;
            }

            return span[start..end];

            static bool IsXmlWhitespace(byte value)
            {
                return value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';
            }
        }

        private static void ValidateDetailXmlFragment(ReadOnlySpan<byte> detailXmlUtf8)
        {
            if (!Utf8.IsValid(detailXmlUtf8))
            {
                throw new ArgumentException("Fault detail XML must be valid UTF-8.", nameof(detailXmlUtf8));
            }

            if (detailXmlUtf8.ContainsAny(s_invalidXml10ControlBytes))
            {
                throw new ArgumentException("Fault detail XML contains invalid XML 1.0 control characters.", nameof(detailXmlUtf8));
            }

            ReadOnlySpan<byte> trimmed = TrimXmlWhitespace(detailXmlUtf8);
            if (trimmed.IsEmpty || trimmed[0] != (byte)'<' || trimmed[^1] != (byte)'>')
            {
                throw new ArgumentException(
                    "Fault detail XML must be a non-empty XML element fragment.",
                    nameof(detailXmlUtf8));
            }

            if (trimmed.IndexOf("<?"u8) >= 0)
            {
                throw new ArgumentException(
                    "Fault detail XML cannot contain processing instructions or XML declarations.",
                    nameof(detailXmlUtf8));
            }

            if (ContainsAsciiCaseInsensitivePrefix(trimmed, "<!doctype"u8) ||
                ContainsAsciiCaseInsensitivePrefix(trimmed, "<!entity"u8))
            {
                throw new ArgumentException(
                    "Fault detail XML cannot contain DTD or ENTITY declarations.",
                    nameof(detailXmlUtf8));
            }
        }

        private static string ValidatePublicReason(string publicReason)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(publicReason);

            return publicReason;
        }

        #endregion

    }

}
