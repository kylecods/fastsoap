using System;
using System.Buffers;
using System.Net;
using System.Text.Unicode;

using CloudNimble.Text.Xml;
using CloudNimble.Text.Xml.Serialization;

using FastSoap.Abstractions;

namespace FastSoap.Core.Serialization
{

    /// <summary>
    /// Encapsulates <c>CloudNimble.Text.Xml</c> (<c>XmlEssentials 1.0.0</c>) options, reader creation, and token
    /// filtering behind a narrow internal boundary per ADR-011.
    /// </summary>
    /// <remarks>
    /// Enforces XML 1.0-only parsing, processing-instruction rejection, bounded reader quotas, and strict
    /// serializer defaults (<c>AllowDuplicateProperties = false</c>, <c>UnmappedMemberHandling = Disallow</c>).
    /// </remarks>
    internal static class XmlEssentialsAdapter
    {

        #region Fields

        private static readonly SearchValues<byte> s_invalidXml10ControlBytes = SearchValues.Create(
        [
            0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
            0x0B, 0x0C,
            0x0E, 0x0F, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
            0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F
        ]);

        private static readonly XmlSerializerOptions s_sharedSerializerOptions = CreateSharedSerializerOptions();
        private static readonly XmlWriterOptions s_sharedWriterOptions = new XmlWriterOptions
        {
            Indented = false,
            OmitXmlDeclaration = true
        };

        private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

        #endregion

        #region Properties

        /// <summary>
        /// Gets the shared read-only <see cref="XmlSerializerOptions"/> configured for strict FastSoap contracts.
        /// </summary>
        internal static XmlSerializerOptions SharedSerializerOptions => s_sharedSerializerOptions;

        /// <summary>
        /// Gets the shared <see cref="XmlWriterOptions"/> configured without an XML declaration for envelope writing.
        /// </summary>
        internal static XmlWriterOptions SharedWriterOptions => s_sharedWriterOptions;

        #endregion

        #region Internal Methods

        /// <summary>
        /// Creates bounded <see cref="XmlReaderOptions"/> from <paramref name="options"/>.
        /// </summary>
        /// <param name="options">The validated endpoint options supplying request and XML quotas.</param>
        /// <returns>An initialized <see cref="XmlReaderOptions"/> enforcing FastSoap security limits.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
        internal static XmlReaderOptions CreateReaderOptions(SoapEndpointOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            return new XmlReaderOptions
            {
                AllowMultipleRoots = false,
                CommentHandling = XmlCommentHandling.Skip,
                MaxAttributeCount = options.MaxAttributesPerElement,
                MaxDepth = options.MaxXmlDepth,
                MaxDocumentByteCount = options.MaxRequestBytes,
                MaxNameByteCount = 1024,
                MaxTextByteCount = options.MaxRequestBytes,
                WhitespaceHandling = XmlWhitespaceHandling.Insignificant
            };
        }

        /// <summary>
        /// Creates a new <see cref="XmlSerializerOptions"/> configured for strict FastSoap contracts.
        /// </summary>
        /// <returns>A fresh <see cref="XmlSerializerOptions"/> instance for binding a single <see cref="XmlSerializerContext"/>.</returns>
        internal static XmlSerializerOptions CreateStrictSerializerOptions()
        {
            return CreateSharedSerializerOptions();
        }

        /// <summary>
        /// Disposes <paramref name="writer"/> while preserving any original exception thrown before normal completion.
        /// </summary>
        /// <param name="writer">The active <see cref="Utf8XmlWriter"/> instance.</param>
        /// <param name="completed"><see langword="true"/> if writing completed normally; <see langword="false"/> during exception unwinding.</param>
        internal static void DisposeSafely(Utf8XmlWriter writer, bool completed)
        {
            if (completed)
            {
                writer.Dispose();

                return;
            }

            try
            {
                writer.Dispose();
            }
            catch
            {
                // Preserve the original serialization or quota exception rather than masking it with
                // Utf8XmlWriter.Dispose()'s unclosed-element check while still releasing internal resources.
            }
        }

        /// <summary>
        /// Verifies that <paramref name="utf8Xml"/> contains no processing instructions anywhere in the document,
        /// including inside deserialized body elements where <c>XmlSerializer.Deserialize</c> would otherwise skip them.
        /// </summary>
        /// <param name="utf8Xml">Complete bounded UTF-8 XML request bytes.</param>
        /// <param name="readerOptions">Bounded reader options.</param>
        /// <exception cref="SoapProtocolException">Thrown with HTTP 400 if any <see cref="XmlTokenType.ProcessingInstruction"/> token is present.</exception>
        internal static void EnsureNoProcessingInstructions(ReadOnlySpan<byte> utf8Xml, XmlReaderOptions readerOptions)
        {
            int firstPiMarker = utf8Xml.IndexOf("<?"u8);
            if (firstPiMarker < 0)
            {
                return;
            }

            // Skip past a single leading <?xml ...?> declaration if present at offset 0 or after UTF-8 BOM;
            // only scan tokens if another "<?" appears or "<?xml" appears later in the document.
            bool isLeadingDeclarationOffset = firstPiMarker == 0 ||
                (firstPiMarker == 3 && utf8Xml.StartsWith(Utf8Bom));
            ReadOnlySpan<byte> afterFirst = utf8Xml[(firstPiMarker + 2)..];
            bool startsWithXmlDecl = isLeadingDeclarationOffset &&
                afterFirst.StartsWith("xml"u8) &&
                (afterFirst.Length == 3 || afterFirst[3] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or (byte)'?');

            if (startsWithXmlDecl && afterFirst.IndexOf("<?"u8) < 0)
            {
                return;
            }

            Span<byte> scratch = stackalloc byte[1024];
            Utf8XmlReader scanReader = new Utf8XmlReader(utf8Xml, scratch, readerOptions);
            while (ReadNextContentToken(ref scanReader))
            {
            }
        }

        /// <summary>
        /// Advances <paramref name="reader"/> to the next significant content token while enforcing XML 1.0
        /// and rejecting processing instructions.
        /// </summary>
        /// <param name="reader">The active <see cref="Utf8XmlReader"/> instance.</param>
        /// <returns><see langword="true"/> if another content token was read; <see langword="false"/> at EOF.</returns>
        /// <exception cref="SoapProtocolException">Thrown with HTTP 400 when XML 1.1 or a processing instruction is encountered.</exception>
        internal static bool ReadNextContentToken(ref Utf8XmlReader reader)
        {
            while (reader.Read())
            {
                if (reader.Version != XmlVersion.Version10)
                {
                    throw new SoapProtocolException(
                        SoapFaultKind.Sender,
                        HttpStatusCode.BadRequest,
                        "Only XML 1.0 is supported.");
                }

                if (reader.TokenType == XmlTokenType.XmlDeclaration)
                {
                    continue;
                }

                if (reader.TokenType == XmlTokenType.ProcessingInstruction)
                {
                    throw new SoapProtocolException(
                        SoapFaultKind.Sender,
                        HttpStatusCode.BadRequest,
                        "Processing instructions are prohibited in SOAP messages.");
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Validates that <paramref name="utf8Xml"/> is valid UTF-8 and contains no C0 control bytes disallowed by XML 1.0.
        /// </summary>
        /// <param name="utf8Xml">Complete bounded UTF-8 XML request bytes.</param>
        /// <exception cref="SoapProtocolException">Thrown with HTTP 400 when invalid UTF-8 or disallowed control bytes are present.</exception>
        internal static void ValidateXml10ControlBytes(ReadOnlySpan<byte> utf8Xml)
        {
            if (!Utf8.IsValid(utf8Xml))
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    HttpStatusCode.BadRequest,
                    "Invalid UTF-8 encoding in SOAP request.");
            }

            if (utf8Xml.ContainsAny(s_invalidXml10ControlBytes))
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    HttpStatusCode.BadRequest,
                    "Invalid XML 1.0 control character in SOAP request.");
            }
        }

        #endregion

        #region Private Methods

        private static XmlSerializerOptions CreateSharedSerializerOptions()
        {
            return new XmlSerializerOptions
            {
                AllowDuplicateProperties = false,
                OmitXmlDeclaration = true,
                UnmappedMemberHandling = XmlUnmappedMemberHandling.Disallow
            };
        }

        #endregion

    }

}
