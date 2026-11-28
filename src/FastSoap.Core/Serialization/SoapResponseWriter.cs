using System;
using System.Buffers;

using CloudNimble.Text.Xml;
using CloudNimble.Text.Xml.Serialization;

using FastSoap.Abstractions;
using FastSoap.Core.Protocol;

namespace FastSoap.Core.Serialization
{

    /// <summary>
    /// Stages or streams complete SOAP 1.1 and SOAP 1.2 response envelopes into an <see cref="IBufferWriter{T}"/>
    /// using <see cref="Utf8XmlWriter"/> without emitting an XML declaration inside the envelope.
    /// </summary>
    /// <remarks>
    /// Declares <c>xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"</c> once on the root <c>Envelope</c>
    /// element so <c>Utf8XmlWriter</c> omits redundant <c>xmlns:xsi</c> declarations on individual
    /// <c>xsi:nil="true"</c> body elements.
    /// </remarks>
    public static class SoapResponseWriter
    {

        #region Public Methods

        /// <summary>
        /// Writes a complete SOAP envelope containing a serialized <typeparamref name="TResponse"/> Body element.
        /// </summary>
        /// <typeparam name="TResponse">The response wire DTO type.</typeparam>
        /// <param name="version">Target SOAP protocol version (<see cref="SoapVersion.Soap11"/> or <see cref="SoapVersion.Soap12"/>).</param>
        /// <param name="destination">The <see cref="IBufferWriter{T}"/> receiving the UTF-8 SOAP envelope.</param>
        /// <param name="response">The typed response DTO instance to serialize inside <c>Body</c>.</param>
        /// <param name="responseTypeInfo">Source-generated <see cref="XmlTypeInfo{T}"/> metadata for <typeparamref name="TResponse"/>.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="version"/> is undefined.</exception>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="destination"/>, <paramref name="response"/>, or <paramref name="responseTypeInfo"/> is <see langword="null"/>.</exception>
        public static void WriteResponse<TResponse>(
            SoapVersion version,
            IBufferWriter<byte> destination,
            TResponse response,
            XmlTypeInfo<TResponse> responseTypeInfo)
        {
            _ = WriteResponseAndCount(version, destination, response, responseTypeInfo);
        }

        #endregion

        #region Internal Methods

        internal static long WriteResponseAndCount<TResponse>(
            SoapVersion version,
            IBufferWriter<byte> destination,
            TResponse response,
            XmlTypeInfo<TResponse> responseTypeInfo)
        {
            if (!Enum.IsDefined(version))
            {
                throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported SOAP version.");
            }

            ArgumentNullException.ThrowIfNull(destination);
            ArgumentNullException.ThrowIfNull(response);
            ArgumentNullException.ThrowIfNull(responseTypeInfo);

            ReadOnlySpan<byte> envelopeNamespace = version == SoapVersion.Soap11
                ? SoapConstants.Soap11NamespaceUtf8
                : SoapConstants.Soap12NamespaceUtf8;

            Utf8XmlWriter writer = new Utf8XmlWriter(destination, XmlEssentialsAdapter.SharedWriterOptions);
            bool completed = false;
            try
            {
                writer.WriteStartElement(SoapConstants.EnvelopePrefixUtf8, SoapConstants.EnvelopeLocalNameUtf8, envelopeNamespace);
                writer.WriteNamespaceDeclaration(SoapConstants.XsiPrefixUtf8, SoapConstants.XsiNamespaceUtf8);
                writer.WriteStartElement(SoapConstants.EnvelopePrefixUtf8, SoapConstants.BodyLocalNameUtf8, envelopeNamespace);

                XmlSerializer.Serialize(writer, response, responseTypeInfo);

                writer.WriteEndElement();
                writer.WriteEndElement();
                writer.Flush();

                long bytesCommitted = writer.BytesCommitted;
                completed = true;

                return bytesCommitted;
            }
            finally
            {
                XmlEssentialsAdapter.DisposeSafely(writer, completed);
            }
        }

        #endregion

    }

}
