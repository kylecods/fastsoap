using System;
using System.Collections.Frozen;
using System.Threading;
using System.Threading.Tasks;
using CloudNimble.Text.Xml;
using CloudNimble.Text.Xml.Serialization;

namespace FastSoap.R0C.GreenfieldSpike.Protocol
{

    /// <summary>
    /// Exception thrown when a SOAP 1.1 request fails envelope structure, action routing, or XML 1.0 profile validation.
    /// </summary>
    public sealed class SoapProtocolException : Exception
    {

        #region Properties

        /// <summary>
        /// Gets the HTTP status code mapped to this failure.
        /// </summary>
        public int HttpStatusCode { get; }

        /// <summary>
        /// Gets a value indicating whether the response should be formatted as a SOAP 1.1 Fault.
        /// </summary>
        public bool EmitSoapFault { get; }

        /// <summary>
        /// Gets the SOAP 1.1 fault code local name (for example, <c>Client</c> or <c>VersionMismatch</c>).
        /// </summary>
        public string FaultCodeLocalName { get; }

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of <see cref="SoapProtocolException"/>.
        /// </summary>
        /// <param name="httpStatusCode">HTTP status code to return.</param>
        /// <param name="emitSoapFault">Whether to emit a SOAP 1.1 Fault envelope.</param>
        /// <param name="faultCodeLocalName">SOAP 1.1 fault code local name.</param>
        /// <param name="message">Safe public fault string.</param>
        /// <param name="innerException">Optional inner exception.</param>
        public SoapProtocolException(
            int httpStatusCode,
            bool emitSoapFault,
            string faultCodeLocalName,
            string message,
            Exception? innerException = null)
            : base(message, innerException)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(faultCodeLocalName);
            ArgumentException.ThrowIfNullOrWhiteSpace(message);

            HttpStatusCode = httpStatusCode;
            EmitSoapFault = emitSoapFault;
            FaultCodeLocalName = faultCodeLocalName;
        }

        #endregion

    }

    /// <summary>
    /// Represents a registered SOAP 1.1 operation capable of synchronously parsing its body payload and asynchronously executing.
    /// </summary>
    public interface ISoapOperation
    {

        #region Properties

        /// <summary>
        /// Gets the SOAPAction URI associated with this operation.
        /// </summary>
        string Action { get; }

        /// <summary>
        /// Gets the expected body child element local name.
        /// </summary>
        string RequestLocalName { get; }

        /// <summary>
        /// Gets the expected body child element namespace URI.
        /// </summary>
        string RequestNamespace { get; }

        #endregion

        #region Public Methods

        /// <summary>
        /// Synchronously deserializes the current Body child element and returns an async execution delegate.
        /// </summary>
        /// <param name="reader">The UTF-8 reader positioned on the Body child start element.</param>
        /// <returns>A prepared operation call that executes and writes the SOAP response body element.</returns>
        IPreparedSoapCall PrepareCall(ref Utf8XmlReader reader);

        #endregion

    }

    /// <summary>
    /// Represents a validated, deserialized SOAP operation call ready to cross the async boundary.
    /// </summary>
    public interface IPreparedSoapCall
    {

        #region Public Methods

        /// <summary>
        /// Executes the typed operation handler and serializes the response body element into <paramref name="writer"/>.
        /// </summary>
        /// <param name="writer">The XML writer positioned inside <c>&lt;s:Body&gt;</c>.</param>
        /// <param name="services">Scoped request service provider.</param>
        /// <param name="cancellationToken">Request cancellation token.</param>
        ValueTask ExecuteAndWriteBodyAsync(Utf8XmlWriter writer, IServiceProvider services, CancellationToken cancellationToken);

        #endregion

    }

    /// <summary>
    /// Strongly-typed SOAP operation definition avoiding reflection and <c>object[]</c> dispatch.
    /// </summary>
    /// <typeparam name="TRequest">Wire request DTO type.</typeparam>
    /// <typeparam name="TResponse">Wire response DTO type.</typeparam>
    public sealed class SoapOperation<TRequest, TResponse> : ISoapOperation
    {

        #region Fields

        private readonly Func<TRequest, IServiceProvider, CancellationToken, ValueTask<TResponse>> _handler;
        private readonly XmlTypeInfo<TRequest> _requestTypeInfo;
        private readonly XmlTypeInfo<TResponse> _responseTypeInfo;

        #endregion

        #region Properties

        /// <inheritdoc />
        public string Action { get; }

        /// <inheritdoc />
        public string RequestLocalName { get; }

        /// <inheritdoc />
        public string RequestNamespace { get; }

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new typed <see cref="SoapOperation{TRequest, TResponse}"/>.
        /// </summary>
        /// <param name="action">Exact ordinal SOAPAction value.</param>
        /// <param name="requestTypeInfo">Source-generated request metadata.</param>
        /// <param name="responseTypeInfo">Source-generated response metadata.</param>
        /// <param name="handler">Typed async operation handler.</param>
        public SoapOperation(
            string action,
            XmlTypeInfo<TRequest> requestTypeInfo,
            XmlTypeInfo<TResponse> responseTypeInfo,
            Func<TRequest, IServiceProvider, CancellationToken, ValueTask<TResponse>> handler)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(action);
            ArgumentNullException.ThrowIfNull(requestTypeInfo);
            ArgumentNullException.ThrowIfNull(responseTypeInfo);
            ArgumentNullException.ThrowIfNull(handler);

            Action = action;
            RequestLocalName = requestTypeInfo.RootName.LocalName;
            RequestNamespace = requestTypeInfo.RootName.NamespaceUri;
            _requestTypeInfo = requestTypeInfo;
            _responseTypeInfo = responseTypeInfo;
            _handler = handler;
        }

        #endregion

        #region Public Methods

        /// <inheritdoc />
        public IPreparedSoapCall PrepareCall(ref Utf8XmlReader reader)
        {
            TRequest? request = XmlSerializer.Deserialize(ref reader, _requestTypeInfo);
            if (request is null)
            {
                throw new SoapProtocolException(500, true, "Client", "SOAP request body payload deserialized to null.");
            }

            return new PreparedCall(request, _handler, _responseTypeInfo);
        }

        #endregion

        private sealed class PreparedCall : IPreparedSoapCall
        {

            #region Fields

            private readonly Func<TRequest, IServiceProvider, CancellationToken, ValueTask<TResponse>> _handler;
            private readonly TRequest _request;
            private readonly XmlTypeInfo<TResponse> _responseTypeInfo;

            #endregion

            #region Constructors

            public PreparedCall(
                TRequest request,
                Func<TRequest, IServiceProvider, CancellationToken, ValueTask<TResponse>> handler,
                XmlTypeInfo<TResponse> responseTypeInfo)
            {
                _request = request;
                _handler = handler;
                _responseTypeInfo = responseTypeInfo;
            }

            #endregion

            #region Public Methods

            public async ValueTask ExecuteAndWriteBodyAsync(Utf8XmlWriter writer, IServiceProvider services, CancellationToken cancellationToken)
            {
                TResponse response = await _handler(_request, services, cancellationToken).ConfigureAwait(false);
                XmlSerializer.Serialize(writer, response, _responseTypeInfo);
            }

            #endregion

        }

    }

    /// <summary>
    /// Bounded, synchronous UTF-8 SOAP 1.1 envelope parser and validator built on <see cref="Utf8XmlReader"/>.
    /// </summary>
    public static class SoapEnvelopeReader
    {

        #region Fields

        private static ReadOnlySpan<byte> BodyLocalNameUtf8 => "Body"u8;
        private static ReadOnlySpan<byte> EnvelopeLocalNameUtf8 => "Envelope"u8;
        private static ReadOnlySpan<byte> HeaderLocalNameUtf8 => "Header"u8;
        private static ReadOnlySpan<byte> MustUnderstandUtf8 => "mustUnderstand"u8;
        private static ReadOnlySpan<byte> Soap11NamespaceUtf8 => "http://schemas.xmlsoap.org/soap/envelope/"u8;
        private static ReadOnlySpan<byte> Soap12NamespaceUtf8 => "http://www.w3.org/2003/05/soap-envelope"u8;

        #endregion

        #region Public Methods

        /// <summary>
        /// Synchronously parses and validates a complete SOAP 1.1 envelope, dispatches by <paramref name="soapAction"/>
        /// and Body QName, deserializes the typed payload inside the reader's live namespace scope, and verifies trailing XML end.
        /// </summary>
        /// <param name="utf8Xml">Complete bounded UTF-8 XML bytes.</param>
        /// <param name="soapAction">Parsed SOAPAction value from HTTP headers.</param>
        /// <param name="operations">Immutable endpoint operation table keyed by ordinal SOAPAction.</param>
        /// <param name="maxRequestBytes">Configured document byte limit.</param>
        /// <returns>The prepared typed call ready for async execution.</returns>
        public static IPreparedSoapCall ParseAndPrepare(
            ReadOnlySpan<byte> utf8Xml,
            string soapAction,
            FrozenDictionary<string, ISoapOperation> operations,
            int maxRequestBytes)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(soapAction);
            ArgumentNullException.ThrowIfNull(operations);

            if (!operations.TryGetValue(soapAction, out ISoapOperation? operation))
            {
                throw new SoapProtocolException(500, true, "Client", $"Unknown SOAPAction '{soapAction}'.");
            }

            XmlReaderOptions readerOptions = new XmlReaderOptions
            {
                AllowMultipleRoots = false,
                CommentHandling = XmlCommentHandling.Skip,
                MaxAttributeCount = 64,
                MaxDepth = 64,
                MaxDocumentByteCount = maxRequestBytes,
                MaxNameByteCount = 1024,
                MaxTextByteCount = maxRequestBytes,
                WhitespaceHandling = XmlWhitespaceHandling.Insignificant,
            };

            Span<byte> scratch = stackalloc byte[1024];
            Utf8XmlReader reader = new Utf8XmlReader(utf8Xml, scratch, readerOptions);

            try
            {
                if (!ReadNextContentToken(ref reader))
                {
                    throw new SoapProtocolException(400, false, "Client", "Empty XML document.");
                }

                if (reader.TokenType != XmlTokenType.StartElement)
                {
                    throw new SoapProtocolException(400, false, "Client", "Expected SOAP Envelope start element.");
                }

                if (reader.NamespaceUriEquals(Soap12NamespaceUtf8) && reader.LocalNameEquals(EnvelopeLocalNameUtf8))
                {
                    throw new SoapProtocolException(500, true, "VersionMismatch", "SOAP 1.2 envelope is not supported on this SOAP 1.1 endpoint.");
                }

                if (!reader.LocalNameEquals(EnvelopeLocalNameUtf8) || !reader.NamespaceUriEquals(Soap11NamespaceUtf8))
                {
                    throw new SoapProtocolException(500, true, "VersionMismatch", "Root element must be SOAP 1.1 Envelope.");
                }

                if (reader.IsEmptyElement)
                {
                    throw new SoapProtocolException(500, true, "Client", "SOAP Envelope must contain a Body element.");
                }

                bool seenHeader = false;
                bool seenBody = false;
                IPreparedSoapCall? preparedCall = null;

                while (ReadNextContentToken(ref reader))
                {
                    if (reader.TokenType == XmlTokenType.EndElement)
                    {
                        break;
                    }

                    if (reader.TokenType != XmlTokenType.StartElement)
                    {
                        throw new SoapProtocolException(500, true, "Client", "Unexpected content inside SOAP Envelope.");
                    }

                    if (!reader.NamespaceUriEquals(Soap11NamespaceUtf8))
                    {
                        throw new SoapProtocolException(500, true, "Client", "Unexpected element namespace inside SOAP Envelope.");
                    }

                    if (reader.LocalNameEquals(HeaderLocalNameUtf8))
                    {
                        if (seenHeader || seenBody)
                        {
                            throw new SoapProtocolException(500, true, "Client", "SOAP Header must appear at most once and before Body.");
                        }

                        seenHeader = true;
                        ValidateHeaders(ref reader);
                        continue;
                    }

                    if (reader.LocalNameEquals(BodyLocalNameUtf8))
                    {
                        if (seenBody)
                        {
                            throw new SoapProtocolException(500, true, "Client", "SOAP Envelope must contain only one Body element.");
                        }

                        seenBody = true;
                        preparedCall = ParseBody(ref reader, operation);
                        continue;
                    }

                    throw new SoapProtocolException(500, true, "Client", "Unexpected child element in SOAP Envelope.");
                }

                if (!seenBody || preparedCall is null)
                {
                    throw new SoapProtocolException(500, true, "Client", "SOAP Envelope is missing a valid Body element.");
                }

                while (ReadNextContentToken(ref reader))
                {
                    throw new SoapProtocolException(400, false, "Client", "Unexpected trailing content after SOAP Envelope.");
                }

                return preparedCall;
            }
            catch (XmlException ex)
            {
                throw new SoapProtocolException(400, false, "Client", "Malformed XML in SOAP request.", ex);
            }
        }

        #endregion

        #region Private Methods

        private static IPreparedSoapCall ParseBody(ref Utf8XmlReader reader, ISoapOperation operation)
        {
            if (reader.IsEmptyElement)
            {
                throw new SoapProtocolException(500, true, "Client", "SOAP Body cannot be empty.");
            }

            if (!ReadNextContentToken(ref reader) || reader.TokenType != XmlTokenType.StartElement)
            {
                throw new SoapProtocolException(500, true, "Client", "Expected operation element inside SOAP Body.");
            }

            if (!reader.LocalNameEquals(operation.RequestLocalName) || !reader.NamespaceUriEquals(operation.RequestNamespace))
            {
                throw new SoapProtocolException(
                    500,
                    true,
                    "Client",
                    $"SOAPAction '{operation.Action}' requires body element '{{{operation.RequestNamespace}}}{operation.RequestLocalName}'.");
            }

            IPreparedSoapCall prepared = operation.PrepareCall(ref reader);

            if (!ReadNextContentToken(ref reader) || reader.TokenType != XmlTokenType.EndElement)
            {
                throw new SoapProtocolException(500, true, "Client", "SOAP Body must contain exactly one operation element.");
            }

            return prepared;
        }

        private static bool ReadNextContentToken(ref Utf8XmlReader reader)
        {
            while (reader.Read())
            {
                if (reader.Version != XmlVersion.Version10)
                {
                    throw new SoapProtocolException(400, false, "Client", "Only XML 1.0 is supported.");
                }

                if (reader.TokenType == XmlTokenType.XmlDeclaration)
                {
                    continue;
                }

                if (reader.TokenType == XmlTokenType.ProcessingInstruction)
                {
                    throw new SoapProtocolException(400, false, "Client", "Processing instructions are prohibited in SOAP messages.");
                }

                return true;
            }

            return false;
        }

        private static void ValidateHeaders(ref Utf8XmlReader reader)
        {
            if (reader.IsEmptyElement)
            {
                return;
            }

            while (ReadNextContentToken(ref reader))
            {
                if (reader.TokenType == XmlTokenType.EndElement)
                {
                    return;
                }

                if (reader.TokenType != XmlTokenType.StartElement)
                {
                    throw new SoapProtocolException(500, true, "Client", "Unexpected content inside SOAP Header.");
                }

                bool mustUnderstand = false;
                while (reader.MoveToNextAttribute())
                {
                    if (reader.NamespaceUriEquals(Soap11NamespaceUtf8) && reader.LocalNameEquals(MustUnderstandUtf8))
                    {
                        if (reader.ValueTextEquals("1"u8))
                        {
                            mustUnderstand = true;
                        }
                        else if (!reader.ValueTextEquals("0"u8))
                        {
                            throw new SoapProtocolException(500, true, "Client", "Invalid SOAP 1.1 mustUnderstand lexical value.");
                        }
                    }
                }

                reader.MoveToElement();
                if (mustUnderstand)
                {
                    throw new SoapProtocolException(500, true, "MustUnderstand", "Mandatory SOAP header is not understood.");
                }

                reader.Skip();
            }
        }

        #endregion

    }

}
