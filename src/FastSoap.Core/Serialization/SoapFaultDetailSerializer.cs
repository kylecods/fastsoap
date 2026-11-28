using System;
using System.Buffers;
using System.Net;

using CloudNimble.Text.Xml;
using CloudNimble.Text.Xml.Serialization;

using FastSoap.Abstractions;
using FastSoap.Core.Buffers;
using FastSoap.Core.Protocol;

namespace FastSoap.Core.Serialization
{

    /// <summary>
    /// Reflection-free, Native AOT-compatible serializer for structured SOAP fault detail payloads
    /// and WCF-compatible <see cref="SoapExceptionDetail"/> elements.
    /// </summary>
    /// <example>
    /// <code>
    /// throw SoapFaultDetailSerializer.CreateFaultException(
    ///     SoapFaultKind.Sender,
    ///     "Payment validation failed.",
    ///     new PaymentFaultDetail { ErrorCode = "INVALID_AMOUNT", Message = "Amount must be positive." },
    ///     PaymentXmlContext.Default.PaymentFaultDetail);
    /// </code>
    /// </example>
    /// <remarks>
    /// Serializes fault detail fragments into a bounded pooled <see cref="BoundedPooledBufferWriter"/> with
    /// <c>OmitXmlDeclaration = true</c> before copying into an owned <see cref="ReadOnlyMemory{T}"/>.
    /// </remarks>
    public static class SoapFaultDetailSerializer
    {

        #region Fields

        private static ReadOnlySpan<byte> ExceptionDetailElementUtf8 => "ExceptionDetail"u8;
        private static ReadOnlySpan<byte> HelpLinkElementUtf8 => "HelpLink"u8;
        private static ReadOnlySpan<byte> InnerExceptionElementUtf8 => "InnerException"u8;
        private static ReadOnlySpan<byte> MessageElementUtf8 => "Message"u8;
        private static ReadOnlySpan<byte> StackTraceElementUtf8 => "StackTrace"u8;
        private static ReadOnlySpan<byte> TypeElementUtf8 => "Type"u8;
        private static ReadOnlySpan<byte> WcfExceptionDetailNamespaceUtf8 => "http://schemas.datacontract.org/2004/07/System.ServiceModel"u8;

        #endregion

        #region Public Methods

        /// <summary>
        /// Serializes <paramref name="detail"/> using source-generated <paramref name="detailTypeInfo"/> and returns
        /// a <see cref="SoapFaultException"/> carrying the resulting UTF-8 detail XML.
        /// </summary>
        /// <typeparam name="TDetail">The typed fault detail DTO type.</typeparam>
        /// <param name="faultKind">Normalized SOAP fault classification.</param>
        /// <param name="publicReason">Safe public fault reason string.</param>
        /// <param name="detail">The typed fault detail instance.</param>
        /// <param name="detailTypeInfo">Source-generated <see cref="XmlTypeInfo{T}"/> metadata for <typeparamref name="TDetail"/>.</param>
        /// <param name="codeNamespace">Optional custom fault code XML namespace URI.</param>
        /// <param name="codeLocalName">Optional custom fault code XML local name.</param>
        /// <param name="actorOrNode">Optional SOAP 1.1 <c>faultactor</c> or SOAP 1.2 <c>Node</c> URI.</param>
        /// <param name="maxBytes">Maximum serialized detail byte budget; defaults to <see cref="SoapConstants.DefaultFaultBytesLimit"/>.</param>
        /// <param name="pool">Optional <see cref="ArrayPool{T}"/> for staging; defaults to <see cref="ArrayPool{T}.Shared"/>.</param>
        /// <param name="clearOnReturn">Whether to zero-clear the staging buffer before returning it to <paramref name="pool"/>.</param>
        /// <param name="innerException">Optional underlying exception for server-side diagnostics.</param>
        /// <param name="httpStatusCode">Optional HTTP error status code override (400-599).</param>
        /// <returns>A validated <see cref="SoapFaultException"/> ready to throw from a service or header handler.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="detail"/> or <paramref name="detailTypeInfo"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="faultKind"/> is undefined or <paramref name="maxBytes"/> is non-positive.</exception>
        /// <exception cref="ResponseQuotaExceededException">Thrown when serialized detail XML exceeds <paramref name="maxBytes"/>.</exception>
        public static SoapFaultException CreateFaultException<TDetail>(
            SoapFaultKind faultKind,
            string publicReason,
            TDetail detail,
            XmlTypeInfo<TDetail> detailTypeInfo,
            string? codeNamespace = null,
            string? codeLocalName = null,
            string? actorOrNode = null,
            int maxBytes = SoapConstants.DefaultFaultBytesLimit,
            ArrayPool<byte>? pool = null,
            bool clearOnReturn = true,
            Exception? innerException = null,
            HttpStatusCode? httpStatusCode = null)
        {
            ReadOnlyMemory<byte> detailXmlUtf8 = Serialize(detail, detailTypeInfo, maxBytes, pool, clearOnReturn);

            return new SoapFaultException(
                faultKind,
                publicReason,
                detailXmlUtf8,
                codeNamespace,
                codeLocalName,
                actorOrNode,
                innerException,
                httpStatusCode);
        }

        /// <summary>
        /// Serializes a WCF-compatible <see cref="SoapExceptionDetail"/> and returns a <see cref="SoapFaultException"/>
        /// carrying the resulting UTF-8 <c>ExceptionDetail</c> XML.
        /// </summary>
        /// <param name="faultKind">Normalized SOAP fault classification.</param>
        /// <param name="publicReason">Safe public fault reason string.</param>
        /// <param name="detail">The WCF-compatible <see cref="SoapExceptionDetail"/> instance.</param>
        /// <param name="codeNamespace">Optional custom fault code XML namespace URI.</param>
        /// <param name="codeLocalName">Optional custom fault code XML local name.</param>
        /// <param name="actorOrNode">Optional SOAP 1.1 <c>faultactor</c> or SOAP 1.2 <c>Node</c> URI.</param>
        /// <param name="maxBytes">Maximum serialized detail byte budget; defaults to <see cref="SoapConstants.DefaultFaultBytesLimit"/>.</param>
        /// <param name="pool">Optional <see cref="ArrayPool{T}"/> for staging; defaults to <see cref="ArrayPool{T}.Shared"/>.</param>
        /// <param name="clearOnReturn">Whether to zero-clear the staging buffer before returning it to <paramref name="pool"/>.</param>
        /// <param name="innerException">Optional underlying exception for server-side diagnostics.</param>
        /// <param name="httpStatusCode">Optional HTTP error status code override (400-599).</param>
        /// <returns>A validated <see cref="SoapFaultException"/> carrying the WCF <c>ExceptionDetail</c> XML.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="detail"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="faultKind"/> is undefined or <paramref name="maxBytes"/> is non-positive.</exception>
        /// <exception cref="ResponseQuotaExceededException">Thrown when serialized detail XML exceeds <paramref name="maxBytes"/>.</exception>
        public static SoapFaultException CreateFaultException(
            SoapFaultKind faultKind,
            string publicReason,
            SoapExceptionDetail detail,
            string? codeNamespace = null,
            string? codeLocalName = null,
            string? actorOrNode = null,
            int maxBytes = SoapConstants.DefaultFaultBytesLimit,
            ArrayPool<byte>? pool = null,
            bool clearOnReturn = true,
            Exception? innerException = null,
            HttpStatusCode? httpStatusCode = null)
        {
            ReadOnlyMemory<byte> detailXmlUtf8 = SerializeExceptionDetail(detail, maxBytes, pool, clearOnReturn);

            return new SoapFaultException(
                faultKind,
                publicReason,
                detailXmlUtf8,
                codeNamespace,
                codeLocalName,
                actorOrNode,
                innerException,
                httpStatusCode);
        }

        /// <summary>
        /// Serializes <paramref name="detail"/> with <paramref name="detailTypeInfo"/> into a bounded pooled writer
        /// and returns owned UTF-8 XML fragment bytes without an XML declaration.
        /// </summary>
        /// <typeparam name="TDetail">The typed fault detail DTO type.</typeparam>
        /// <param name="detail">The typed fault detail instance.</param>
        /// <param name="detailTypeInfo">Source-generated <see cref="XmlTypeInfo{T}"/> metadata for <typeparamref name="TDetail"/>.</param>
        /// <param name="maxBytes">Maximum serialized detail byte budget; defaults to <see cref="SoapConstants.DefaultFaultBytesLimit"/>.</param>
        /// <param name="pool">Optional <see cref="ArrayPool{T}"/> for staging; defaults to <see cref="ArrayPool{T}.Shared"/>.</param>
        /// <param name="clearOnReturn">Whether to zero-clear the staging buffer before returning it to <paramref name="pool"/>.</param>
        /// <returns>Owned UTF-8 XML bytes for the serialized <typeparamref name="TDetail"/> element.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="detail"/> or <paramref name="detailTypeInfo"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxBytes"/> is less than or equal to zero.</exception>
        /// <exception cref="ResponseQuotaExceededException">Thrown when serialized detail XML exceeds <paramref name="maxBytes"/>.</exception>
        public static ReadOnlyMemory<byte> Serialize<TDetail>(
            TDetail detail,
            XmlTypeInfo<TDetail> detailTypeInfo,
            int maxBytes = SoapConstants.DefaultFaultBytesLimit,
            ArrayPool<byte>? pool = null,
            bool clearOnReturn = true)
        {
            ArgumentNullException.ThrowIfNull(detail);
            ArgumentNullException.ThrowIfNull(detailTypeInfo);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxBytes, 0);

            using BoundedPooledBufferWriter writer = new BoundedPooledBufferWriter(
                Math.Min(512, maxBytes),
                maxBytes,
                clearOnReturn,
                pool);

            Utf8XmlWriter xmlWriter = new Utf8XmlWriter(writer, XmlEssentialsAdapter.SharedWriterOptions);
            bool completed = false;
            try
            {
                XmlSerializer.Serialize(xmlWriter, detail, detailTypeInfo);
                xmlWriter.Flush();
                completed = true;
            }
            finally
            {
                XmlEssentialsAdapter.DisposeSafely(xmlWriter, completed);
            }

            return writer.WrittenSpan.ToArray();
        }

        /// <summary>
        /// Serializes a <see cref="SoapExceptionDetail"/> directly in UTF-8 using the canonical WCF
        /// <c>http://schemas.datacontract.org/2004/07/System.ServiceModel</c> <c>ExceptionDetail</c> wire format.
        /// </summary>
        /// <param name="detail">The <see cref="SoapExceptionDetail"/> instance to serialize.</param>
        /// <param name="maxBytes">Maximum serialized detail byte budget; defaults to <see cref="SoapConstants.DefaultFaultBytesLimit"/>.</param>
        /// <param name="pool">Optional <see cref="ArrayPool{T}"/> for staging; defaults to <see cref="ArrayPool{T}.Shared"/>.</param>
        /// <param name="clearOnReturn">Whether to zero-clear the staging buffer before returning it to <paramref name="pool"/>.</param>
        /// <returns>Owned UTF-8 XML bytes for the serialized <c>ExceptionDetail</c> element.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="detail"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxBytes"/> is less than or equal to zero.</exception>
        /// <exception cref="ResponseQuotaExceededException">Thrown when serialized detail XML exceeds <paramref name="maxBytes"/>.</exception>
        public static ReadOnlyMemory<byte> SerializeExceptionDetail(
            SoapExceptionDetail detail,
            int maxBytes = SoapConstants.DefaultFaultBytesLimit,
            ArrayPool<byte>? pool = null,
            bool clearOnReturn = true)
        {
            ArgumentNullException.ThrowIfNull(detail);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxBytes, 0);

            using BoundedPooledBufferWriter writer = new BoundedPooledBufferWriter(
                Math.Min(512, maxBytes),
                maxBytes,
                clearOnReturn,
                pool);

            Utf8XmlWriter xmlWriter = new Utf8XmlWriter(writer, XmlEssentialsAdapter.SharedWriterOptions);
            bool completed = false;
            try
            {
                WriteExceptionDetailElement(
                    xmlWriter,
                    ExceptionDetailElementUtf8,
                    detail,
                    isRoot: true,
                    depth: 0);
                xmlWriter.Flush();
                completed = true;
            }
            finally
            {
                XmlEssentialsAdapter.DisposeSafely(xmlWriter, completed);
            }

            return writer.WrittenSpan.ToArray();
        }

        #endregion

        #region Private Methods

        private static void WriteExceptionDetailElement(
            Utf8XmlWriter writer,
            ReadOnlySpan<byte> elementLocalNameUtf8,
            SoapExceptionDetail detail,
            bool isRoot,
            int depth)
        {
            if (depth >= 32)
            {
                throw new InvalidOperationException("SoapExceptionDetail inner exception chain exceeded maximum depth of 32.");
            }

            if (isRoot)
            {
                writer.WriteStartElement(elementLocalNameUtf8, WcfExceptionDetailNamespaceUtf8);
                writer.WriteNamespaceDeclaration(SoapConstants.XsiPrefixUtf8, SoapConstants.XsiNamespaceUtf8);
            }
            else
            {
                writer.WriteStartElement(elementLocalNameUtf8);
            }

            WriteNullableStringElement(writer, HelpLinkElementUtf8, detail.HelpLink);

            if (detail.InnerException is not null)
            {
                WriteExceptionDetailElement(
                    writer,
                    InnerExceptionElementUtf8,
                    detail.InnerException,
                    isRoot: false,
                    depth + 1);
            }
            else
            {
                writer.WriteNilElement(InnerExceptionElementUtf8);
            }

            WriteNullableStringElement(writer, MessageElementUtf8, detail.Message);
            WriteNullableStringElement(writer, StackTraceElementUtf8, detail.StackTrace);
            WriteNullableStringElement(writer, TypeElementUtf8, detail.Type);

            writer.WriteEndElement();
        }

        private static void WriteNullableStringElement(
            Utf8XmlWriter writer,
            ReadOnlySpan<byte> localNameUtf8,
            string? value)
        {
            if (value is null)
            {
                writer.WriteNilElement(localNameUtf8);

                return;
            }

            writer.WriteElementString(localNameUtf8, value);
        }

        #endregion

    }

}
