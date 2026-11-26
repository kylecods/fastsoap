using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using CloudNimble.Text.Xml;

using FastSoap.Abstractions;
using FastSoap.Core.Buffers;
using FastSoap.Core.Protocol;

namespace FastSoap.Core.Serialization
{

    /// <summary>
    /// Stages safe, standards-compliant SOAP 1.1 and SOAP 1.2 Fault envelopes into an <see cref="IBufferWriter{T}"/>
    /// without leaking internal exception messages or stack traces.
    /// </summary>
    /// <remarks>
    /// SOAP 1.1 faults emit <c>faultcode</c>, <c>faultstring</c>, optional <c>faultactor</c>, and optional <c>detail</c>.
    /// SOAP 1.2 faults emit <c>Code/Value</c>, <c>Reason/Text</c> with <c>xml:lang="en-US"</c>, optional <c>Node</c>,
    /// optional <c>Role</c>, optional <c>Detail</c>, and optional <c>NotUnderstood</c> header blocks.
    /// </remarks>
    public static class SoapFaultWriter
    {

        #region Fields

        private const string GenericInternalFaultReason = "An internal server error occurred while processing the SOAP request.";
        private const string GenericResponseQuotaReason = "The SOAP response exceeded the configured response size limit.";
        private const string XmlNamespace = "http://www.w3.org/XML/1998/namespace";

        #endregion

        #region Public Methods

        /// <summary>
        /// Creates a safe <see cref="SoapFault"/> from an exception without exposing raw internal exception messages.
        /// </summary>
        /// <param name="version">Target SOAP protocol version.</param>
        /// <param name="exception">The exception raised during processing.</param>
        /// <param name="correlationId">Optional correlation identifier.</param>
        /// <returns>A sanitized <see cref="SoapFault"/> suitable for wire serialization.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="version"/> is undefined.</exception>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="exception"/> is <see langword="null"/>.</exception>
        public static SoapFault CreateSafeFaultForException(
            SoapVersion version,
            Exception exception,
            string? correlationId = null)
        {
            if (!Enum.IsDefined(version))
            {
                throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported SOAP version.");
            }

            ArgumentNullException.ThrowIfNull(exception);

            if (exception is SoapProtocolException protocolException)
            {
                return SoapFault.CreateStandard(
                    version,
                    protocolException.FaultKind,
                    protocolException.PublicReason,
                    correlationId: correlationId);
            }

            if (exception is ResponseQuotaExceededException)
            {
                return SoapFault.CreateStandard(
                    version,
                    SoapFaultKind.Receiver,
                    GenericResponseQuotaReason,
                    correlationId: correlationId);
            }

            return SoapFault.CreateStandard(
                version,
                SoapFaultKind.Receiver,
                GenericInternalFaultReason,
                correlationId: correlationId);
        }

        /// <summary>
        /// Writes a complete SOAP 1.1 or SOAP 1.2 Fault envelope for <paramref name="fault"/> into <paramref name="destination"/>.
        /// </summary>
        /// <param name="fault">The normalized, safe <see cref="SoapFault"/> model to write.</param>
        /// <param name="destination">The <see cref="IBufferWriter{T}"/> receiving the UTF-8 fault envelope.</param>
        /// <param name="notUnderstoodHeaders">Optional qualified names of mandatory headers not understood (used for SOAP 1.2 <c>NotUnderstood</c> headers).</param>
        /// <param name="soap12Role">Optional SOAP 1.2 <c>Role</c> URI.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="fault"/> or <paramref name="destination"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="soap12Role"/> is whitespace or <see cref="SoapFault.DetailXmlUtf8"/> is invalid XML.</exception>
        public static void WriteFault(
            SoapFault fault,
            IBufferWriter<byte> destination,
            IReadOnlyList<SoapQName>? notUnderstoodHeaders = null,
            string? soap12Role = null)
        {
            ArgumentNullException.ThrowIfNull(fault);
            ArgumentNullException.ThrowIfNull(destination);

            if (soap12Role is not null)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(soap12Role);
            }

            if (!fault.DetailXmlUtf8.IsEmpty)
            {
                ValidateDetailXml(fault.DetailXmlUtf8.Span);
            }

            if (fault.Version == SoapVersion.Soap11)
            {
                WriteSoap11Fault(fault, destination);
            }
            else
            {
                WriteSoap12Fault(fault, destination, notUnderstoodHeaders, soap12Role);
            }
        }

        #endregion

        #region Private Methods

        private static void ValidateDetailXml(ReadOnlySpan<byte> detailXmlUtf8)
        {
            XmlReaderOptions detailOptions = new XmlReaderOptions
            {
                AllowMultipleRoots = true,
                CommentHandling = XmlCommentHandling.Disallow,
                MaxAttributeCount = 64,
                MaxDepth = 32,
                MaxDocumentByteCount = detailXmlUtf8.Length,
                MaxNameByteCount = 1024,
                MaxTextByteCount = detailXmlUtf8.Length,
                WhitespaceHandling = XmlWhitespaceHandling.Insignificant
            };

            Span<byte> scratch = stackalloc byte[512];
            Utf8XmlReader reader = new Utf8XmlReader(detailXmlUtf8, scratch, detailOptions);

            bool hasElement = false;
            while (reader.Read())
            {
                if (reader.Version != XmlVersion.Version10)
                {
                    throw new ArgumentException("Fault detail XML must use XML 1.0.", nameof(SoapFault.DetailXmlUtf8));
                }

                if (reader.TokenType is XmlTokenType.ProcessingInstruction or XmlTokenType.XmlDeclaration)
                {
                    throw new ArgumentException("Fault detail XML cannot contain processing instructions or declarations.", nameof(SoapFault.DetailXmlUtf8));
                }

                if (reader.TokenType == XmlTokenType.StartElement)
                {
                    hasElement = true;
                }
            }

            if (!hasElement)
            {
                throw new ArgumentException("Fault detail XML must contain at least one element.", nameof(SoapFault.DetailXmlUtf8));
            }
        }

        private static void WriteNotUnderstoodHeaders(
            Utf8XmlWriter writer,
            IReadOnlyList<SoapQName> notUnderstoodHeaders)
        {
            writer.WriteStartElement(
                SoapConstants.EnvelopePrefixUtf8,
                SoapConstants.HeaderLocalNameUtf8,
                SoapConstants.Soap12NamespaceUtf8);

            for (int index = 0; index < notUnderstoodHeaders.Count; index++)
            {
                SoapQName headerName = notUnderstoodHeaders[index];
                writer.WriteStartElement(
                    SoapConstants.EnvelopePrefixUtf8,
                    SoapConstants.NotUnderstoodLocalNameUtf8,
                    SoapConstants.Soap12NamespaceUtf8);

                if (string.IsNullOrEmpty(headerName.NamespaceUri))
                {
                    writer.WriteAttributeString("qname", headerName.LocalName);
                }
                else
                {
                    string prefix = string.Create(CultureInfo.InvariantCulture, $"h{index}");
                    writer.WriteNamespaceDeclaration(prefix, headerName.NamespaceUri);
                    writer.WriteAttributeString("qname", $"{prefix}:{headerName.LocalName}");
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        private static void WriteQualifiedFaultCodeValue(
            Utf8XmlWriter writer,
            SoapFault fault,
            string standardEnvelopeNamespace)
        {
            if (string.Equals(fault.CodeNamespace, standardEnvelopeNamespace, StringComparison.Ordinal))
            {
                writer.WriteStringValue($"s:{fault.CodeLocalName}");
            }
            else
            {
                writer.WriteNamespaceDeclaration("fc"u8, Encoding.UTF8.GetBytes(fault.CodeNamespace));
                writer.WriteStringValue($"fc:{fault.CodeLocalName}");
            }
        }

        private static void WriteSoap11Fault(SoapFault fault, IBufferWriter<byte> destination)
        {
            using Utf8XmlWriter writer = new Utf8XmlWriter(destination, XmlEssentialsAdapter.SharedWriterOptions);
            writer.WriteStartElement(
                SoapConstants.EnvelopePrefixUtf8,
                SoapConstants.EnvelopeLocalNameUtf8,
                SoapConstants.Soap11NamespaceUtf8);
            writer.WriteStartElement(
                SoapConstants.EnvelopePrefixUtf8,
                SoapConstants.BodyLocalNameUtf8,
                SoapConstants.Soap11NamespaceUtf8);
            writer.WriteStartElement(
                SoapConstants.EnvelopePrefixUtf8,
                SoapConstants.FaultLocalNameUtf8,
                SoapConstants.Soap11NamespaceUtf8);

            writer.WriteStartElement("faultcode"u8);
            WriteQualifiedFaultCodeValue(writer, fault, SoapConstants.Soap11Namespace);
            writer.WriteEndElement();

            writer.WriteStartElement("faultstring"u8);
            writer.WriteStringValue(fault.Reason);
            writer.WriteEndElement();

            if (fault.ActorOrNode is not null)
            {
                writer.WriteStartElement("faultactor"u8);
                writer.WriteStringValue(fault.ActorOrNode);
                writer.WriteEndElement();
            }

            if (!fault.DetailXmlUtf8.IsEmpty)
            {
                writer.WriteStartElement("detail"u8);
                writer.WriteRawValue(fault.DetailXmlUtf8.Span, skipInputValidation: false);
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.Flush();
        }

        private static void WriteSoap12Fault(
            SoapFault fault,
            IBufferWriter<byte> destination,
            IReadOnlyList<SoapQName>? notUnderstoodHeaders,
            string? soap12Role)
        {
            using Utf8XmlWriter writer = new Utf8XmlWriter(destination, XmlEssentialsAdapter.SharedWriterOptions);
            writer.WriteStartElement(
                SoapConstants.EnvelopePrefixUtf8,
                SoapConstants.EnvelopeLocalNameUtf8,
                SoapConstants.Soap12NamespaceUtf8);

            if (fault.Kind == SoapFaultKind.MustUnderstand &&
                notUnderstoodHeaders is { Count: > 0 })
            {
                WriteNotUnderstoodHeaders(writer, notUnderstoodHeaders);
            }

            writer.WriteStartElement(
                SoapConstants.EnvelopePrefixUtf8,
                SoapConstants.BodyLocalNameUtf8,
                SoapConstants.Soap12NamespaceUtf8);
            writer.WriteStartElement(
                SoapConstants.EnvelopePrefixUtf8,
                SoapConstants.FaultLocalNameUtf8,
                SoapConstants.Soap12NamespaceUtf8);

            writer.WriteStartElement(SoapConstants.EnvelopePrefixUtf8, "Code"u8, SoapConstants.Soap12NamespaceUtf8);
            writer.WriteStartElement(SoapConstants.EnvelopePrefixUtf8, "Value"u8, SoapConstants.Soap12NamespaceUtf8);
            WriteQualifiedFaultCodeValue(writer, fault, SoapConstants.Soap12Namespace);
            writer.WriteEndElement();
            writer.WriteEndElement();

            writer.WriteStartElement(SoapConstants.EnvelopePrefixUtf8, "Reason"u8, SoapConstants.Soap12NamespaceUtf8);
            writer.WriteStartElement(SoapConstants.EnvelopePrefixUtf8, "Text"u8, SoapConstants.Soap12NamespaceUtf8);
            writer.WriteAttributeString("xml", "lang", XmlNamespace, "en-US");
            writer.WriteStringValue(fault.Reason);
            writer.WriteEndElement();
            writer.WriteEndElement();

            if (fault.ActorOrNode is not null)
            {
                writer.WriteStartElement(SoapConstants.EnvelopePrefixUtf8, "Node"u8, SoapConstants.Soap12NamespaceUtf8);
                writer.WriteStringValue(fault.ActorOrNode);
                writer.WriteEndElement();
            }

            if (soap12Role is not null)
            {
                writer.WriteStartElement(SoapConstants.EnvelopePrefixUtf8, "Role"u8, SoapConstants.Soap12NamespaceUtf8);
                writer.WriteStringValue(soap12Role);
                writer.WriteEndElement();
            }

            if (!fault.DetailXmlUtf8.IsEmpty)
            {
                writer.WriteStartElement(SoapConstants.EnvelopePrefixUtf8, "Detail"u8, SoapConstants.Soap12NamespaceUtf8);
                writer.WriteRawValue(fault.DetailXmlUtf8.Span, skipInputValidation: false);
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.Flush();
        }

        #endregion

    }

}
