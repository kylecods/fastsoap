using System;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using CloudNimble.Text.Xml;
using FastSoap.R0C.GreenfieldSpike.Buffers;
using Microsoft.AspNetCore.Http;

namespace FastSoap.R0C.GreenfieldSpike.Protocol
{

    /// <summary>
    /// Stages and writes SOAP 1.1 responses and SOAP 1.1 Fault envelopes via <see cref="Utf8XmlWriter"/>.
    /// </summary>
    public static class SoapResponseWriter
    {

        #region Fields

        private const string Soap11ContentType = "text/xml; charset=utf-8";
        private static ReadOnlySpan<byte> Soap11NamespaceUtf8 => "http://schemas.xmlsoap.org/soap/envelope/"u8;
        private static ReadOnlySpan<byte> Soap11PrefixUtf8 => "s"u8;

        private static readonly XmlWriterOptions WriterOptions = new XmlWriterOptions
        {
            Indented = false,
            OmitXmlDeclaration = true,
        };

        #endregion

        #region Public Methods

        /// <summary>
        /// Writes a staged SOAP 1.1 Fault response without leaking internal exception details.
        /// </summary>
        /// <param name="httpContext">Current HTTP context.</param>
        /// <param name="statusCode">HTTP status code to commit.</param>
        /// <param name="faultCodeLocalName">SOAP 1.1 fault code local name (for example, <c>Client</c>).</param>
        /// <param name="faultString">Safe public fault message.</param>
        /// <param name="maxFaultBytes">Maximum staged fault payload size in bytes.</param>
        /// <param name="cancellationToken">Cancellation token for pipe write.</param>
        public static async ValueTask WriteFaultAsync(
            HttpContext httpContext,
            int statusCode,
            string faultCodeLocalName,
            string faultString,
            int maxFaultBytes,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(httpContext);
            ArgumentException.ThrowIfNullOrWhiteSpace(faultCodeLocalName);
            ArgumentException.ThrowIfNullOrWhiteSpace(faultString);

            if (httpContext.Response.HasStarted)
            {
                return;
            }

            using BoundedPooledBufferWriter staging = new BoundedPooledBufferWriter(512, maxFaultBytes, clearOnReturn: false);
            using (Utf8XmlWriter writer = new Utf8XmlWriter(staging, WriterOptions))
            {
                writer.WriteStartElement(Soap11PrefixUtf8, "Envelope"u8, Soap11NamespaceUtf8);
                writer.WriteStartElement(Soap11PrefixUtf8, "Body"u8, Soap11NamespaceUtf8);
                writer.WriteStartElement(Soap11PrefixUtf8, "Fault"u8, Soap11NamespaceUtf8);

                writer.WriteStartElement("faultcode"u8);
                writer.WriteStringValue($"s:{faultCodeLocalName}");
                writer.WriteEndElement();

                writer.WriteStartElement("faultstring"u8);
                writer.WriteStringValue(faultString);
                writer.WriteEndElement();

                writer.WriteEndElement();
                writer.WriteEndElement();
                writer.WriteEndElement();
                writer.Flush();
            }

            await CommitStagedAsync(httpContext.Response, statusCode, staging.WrittenMemory, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Executes the prepared SOAP call, stages the full SOAP 1.1 envelope into a bounded pooled buffer,
        /// and commits the response to Kestrel only after serialization completes cleanly.
        /// </summary>
        /// <param name="httpContext">Current HTTP context.</param>
        /// <param name="preparedCall">Prepared typed SOAP call.</param>
        /// <param name="maxResponseBytes">Configured response staging quota.</param>
        /// <param name="clearOnReturn">Whether to clear pooled staging bytes on return.</param>
        /// <param name="cancellationToken">Request cancellation token.</param>
        public static async ValueTask WriteSuccessAsync(
            HttpContext httpContext,
            IPreparedSoapCall preparedCall,
            int maxResponseBytes,
            bool clearOnReturn,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(httpContext);
            ArgumentNullException.ThrowIfNull(preparedCall);

            using BoundedPooledBufferWriter staging = new BoundedPooledBufferWriter(4096, maxResponseBytes, clearOnReturn);
            using (Utf8XmlWriter writer = new Utf8XmlWriter(staging, WriterOptions))
            {
                writer.WriteStartElement(Soap11PrefixUtf8, "Envelope"u8, Soap11NamespaceUtf8);
                writer.WriteStartElement(Soap11PrefixUtf8, "Body"u8, Soap11NamespaceUtf8);

                await preparedCall.ExecuteAndWriteBodyAsync(writer, httpContext.RequestServices, cancellationToken).ConfigureAwait(false);

                writer.WriteEndElement();
                writer.WriteEndElement();
                writer.Flush();
            }

            await CommitStagedAsync(httpContext.Response, StatusCodes.Status200OK, staging.WrittenMemory, cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region Private Methods

        private static async ValueTask CommitStagedAsync(
            HttpResponse response,
            int statusCode,
            ReadOnlyMemory<byte> stagedBytes,
            CancellationToken cancellationToken)
        {
            response.StatusCode = statusCode;
            response.Headers.ContentType = Soap11ContentType;
            response.ContentLength = stagedBytes.Length;

            PipeWriter bodyWriter = response.BodyWriter;
            if (!stagedBytes.IsEmpty)
            {
                stagedBytes.Span.CopyTo(bodyWriter.GetSpan(stagedBytes.Length));
                bodyWriter.Advance(stagedBytes.Length);
            }

            await bodyWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        #endregion

    }

}
