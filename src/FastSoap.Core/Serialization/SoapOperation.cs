using System;
using System.Buffers;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using CloudNimble.Text.Xml;
using CloudNimble.Text.Xml.Serialization;

using FastSoap.Abstractions;
using FastSoap.Core.Buffers;

namespace FastSoap.Core.Serialization
{

    /// <summary>
    /// Strongly typed SOAP operation binding a <see cref="SoapOperationDescriptor"/>, source-generated
    /// <see cref="XmlTypeInfo{T}"/> metadata, and a <see cref="SoapOperationHandler{TRequest, TResponse}"/>.
    /// </summary>
    /// <typeparam name="TRequest">The request wire DTO type.</typeparam>
    /// <typeparam name="TResponse">The response wire DTO type.</typeparam>
    /// <example>
    /// <code>
    /// ISoapOperation operation = new SoapOperation&lt;EchoRequest, EchoResponse&gt;(
    ///     descriptor,
    ///     EchoXmlContext.Default.EchoRequest,
    ///     EchoXmlContext.Default.EchoResponse,
    ///     static (req, ctx, ct) =&gt; ValueTask.FromResult(new EchoResponse { EchoResult = req.Message }));
    /// </code>
    /// </example>
    /// <remarks>
    /// Eliminates <c>object[]</c> parameter arrays, boxing, and runtime reflection on the hot dispatch path.
    /// </remarks>
    public sealed class SoapOperation<TRequest, TResponse> : ISoapOperation
    {

        #region Fields

        private readonly SoapOperationHandler<TRequest, TResponse> _handler;
        private readonly XmlTypeInfo<TRequest> _requestTypeInfo;
        private readonly XmlTypeInfo<TResponse> _responseTypeInfo;

        #endregion

        #region Properties

        /// <inheritdoc />
        public SoapOperationDescriptor Descriptor { get; }

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new typed <see cref="SoapOperation{TRequest, TResponse}"/>.
        /// </summary>
        /// <param name="descriptor">The validated operation descriptor.</param>
        /// <param name="requestTypeInfo">The source-generated <see cref="XmlTypeInfo{T}"/> for <typeparamref name="TRequest"/>.</param>
        /// <param name="responseTypeInfo">The source-generated <see cref="XmlTypeInfo{T}"/> for <typeparamref name="TResponse"/>.</param>
        /// <param name="handler">The typed asynchronous operation handler delegate.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null"/>.</exception>
        public SoapOperation(
            SoapOperationDescriptor descriptor,
            XmlTypeInfo<TRequest> requestTypeInfo,
            XmlTypeInfo<TResponse> responseTypeInfo,
            SoapOperationHandler<TRequest, TResponse> handler)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            ArgumentNullException.ThrowIfNull(requestTypeInfo);
            ArgumentNullException.ThrowIfNull(responseTypeInfo);
            ArgumentNullException.ThrowIfNull(handler);

            Descriptor = descriptor;
            _requestTypeInfo = requestTypeInfo;
            _responseTypeInfo = responseTypeInfo;
            _handler = handler;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Synchronously validates the exact UTF-8 root qualified name against <see cref="Descriptor"/>
        /// and deserializes <typeparamref name="TRequest"/> from <paramref name="reader"/>.
        /// </summary>
        /// <param name="reader">The <see cref="Utf8XmlReader"/> positioned on the Body child start element.</param>
        /// <param name="context">The per-request <see cref="SoapCallContext"/>.</param>
        /// <returns>A <see cref="PreparedSoapCall"/> holding the deserialized request and call context.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is <see langword="null"/>.</exception>
        /// <exception cref="SoapProtocolException">Thrown when the element QName mismatches or payload deserialization fails.</exception>
        public PreparedSoapCall Prepare(ref Utf8XmlReader reader, SoapCallContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (reader.TokenType != XmlTokenType.StartElement ||
                !Descriptor.RequestElement.Matches(reader.NamespaceUriSpan, reader.LocalNameSpan))
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    GetSenderFaultStatusCode(context.Version),
                    "SOAP Body element does not match the expected operation qualified name.",
                    context.Version);
            }

            TRequest? request;
            try
            {
                request = XmlSerializer.Deserialize(ref reader, _requestTypeInfo);
            }
            catch (XmlException ex) when (!string.IsNullOrWhiteSpace(ex.Path))
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    GetSenderFaultStatusCode(context.Version),
                    "SOAP request payload failed schema or type validation.",
                    context.Version,
                    ex);
            }
            catch (XmlException)
            {
                throw;
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException or OverflowException or ArgumentException)
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    GetSenderFaultStatusCode(context.Version),
                    "SOAP request payload failed schema or type validation.",
                    context.Version,
                    ex);
            }

            if (request is null)
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    GetSenderFaultStatusCode(context.Version),
                    "SOAP request payload deserialized to null.",
                    context.Version);
            }

            return new TypedPreparedSoapCall(request, context, Descriptor, _handler, _responseTypeInfo);
        }

        #endregion

        #region Private Methods

        private static HttpStatusCode GetSenderFaultStatusCode(SoapVersion version)
        {
            return version == SoapVersion.Soap11
                ? HttpStatusCode.InternalServerError
                : HttpStatusCode.BadRequest;
        }

        #endregion

        private sealed class TypedPreparedSoapCall : PreparedSoapCall
        {

            #region Fields

            private readonly SoapCallContext _context;
            private readonly SoapOperationDescriptor _descriptor;
            private readonly SoapOperationHandler<TRequest, TResponse> _handler;
            private readonly TRequest _request;
            private TResponse? _response;
            private readonly XmlTypeInfo<TResponse> _responseTypeInfo;

            #endregion

            #region Properties

            public override SoapCallContext Context => _context;

            public override SoapOperationDescriptor Descriptor => _descriptor;

            internal override bool SupportsSplitExecution => true;

            #endregion

            #region Constructors

            public TypedPreparedSoapCall(
                TRequest request,
                SoapCallContext context,
                SoapOperationDescriptor descriptor,
                SoapOperationHandler<TRequest, TResponse> handler,
                XmlTypeInfo<TResponse> responseTypeInfo)
            {
                _request = request;
                _context = context;
                _descriptor = descriptor;
                _handler = handler;
                _responseTypeInfo = responseTypeInfo;
            }

            #endregion

            #region Public Methods

            public override async ValueTask ExecuteAndWriteAsync(
                BoundedPooledBufferWriter destination,
                CancellationToken cancellationToken)
            {
                ArgumentNullException.ThrowIfNull(destination);

                TResponse response = await InvokeHandlerAsync(cancellationToken).ConfigureAwait(false);
                SoapResponseWriter.WriteResponse(_context.Version, destination, response, _responseTypeInfo);
            }

            #endregion

            #region Internal Methods

            internal override async ValueTask ExecuteAsync(CancellationToken cancellationToken)
            {
                _response = await InvokeHandlerAsync(cancellationToken).ConfigureAwait(false);
            }

            internal override long WriteDirectResponse(IBufferWriter<byte> destination)
            {
                ArgumentNullException.ThrowIfNull(destination);

                TResponse response = _response ?? throw new InvalidOperationException("Operation handler has not been executed.");

                return SoapResponseWriter.WriteResponseAndCount(_context.Version, destination, response, _responseTypeInfo);
            }

            #endregion

            #region Private Methods

            private async ValueTask<TResponse> InvokeHandlerAsync(CancellationToken cancellationToken)
            {
                TResponse response = await _handler(_request, _context, cancellationToken).ConfigureAwait(false);
                if (response is null)
                {
                    throw new SoapProtocolException(
                        SoapFaultKind.Receiver,
                        HttpStatusCode.InternalServerError,
                        "SOAP operation returned a null response.",
                        _context.Version);
                }

                return response;
            }

            #endregion

        }

    }

}
