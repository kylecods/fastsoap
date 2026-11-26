using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Text;

using CloudNimble.Text.Xml;

using FastSoap.Abstractions;
using FastSoap.Core.Dispatching;
using FastSoap.Core.Serialization;

namespace FastSoap.Core.Protocol
{

    /// <summary>
    /// Synchronously parses, validates, and dispatches complete UTF-8 SOAP 1.1 and SOAP 1.2 envelopes
    /// using <see cref="Utf8XmlReader"/> within a single namespace-preserving pass.
    /// </summary>
    /// <remarks>
    /// Enforces XML 1.0 only, prohibits DTDs, processing instructions, and <c>encodingStyle</c>, distinguishes
    /// non-<c>Envelope</c> roots (HTTP <c>400</c>) from wrong-namespace <c>Envelope</c> roots (<c>VersionMismatch</c>),
    /// bounds header count and header byte size, and validates exact UTF-8 Body QNames prior to deserialization.
    /// </remarks>
    public static class SoapEnvelopeReader
    {

        #region Fields

        private static ReadOnlySpan<byte> RelayLocalNameUtf8 => "relay"u8;

        #endregion

        #region Public Methods

        /// <summary>
        /// Synchronously parses and validates <paramref name="utf8Xml"/>, extracts owned headers, resolves the target
        /// operation, and deserializes the typed request payload into a <see cref="PreparedSoapCall"/>.
        /// </summary>
        /// <param name="utf8Xml">Complete bounded UTF-8 XML request bytes.</param>
        /// <param name="expectedVersion">The SOAP protocol version negotiated from HTTP transport metadata.</param>
        /// <param name="action">The resolved SOAP action URI, or <see langword="null"/> when omitted and permitted.</param>
        /// <param name="operations">The immutable endpoint operation table.</param>
        /// <param name="options">The validated endpoint options supplying XML and header quotas.</param>
        /// <param name="services">The request-scoped <see cref="IServiceProvider"/>.</param>
        /// <param name="user">The authenticated <see cref="ClaimsPrincipal"/> for the request.</param>
        /// <returns>A <see cref="PreparedSoapCall"/> containing the deserialized request and owned headers.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="expectedVersion"/> is undefined.</exception>
        /// <exception cref="ArgumentNullException">Thrown when required object parameters are <see langword="null"/>.</exception>
        /// <exception cref="SoapProtocolException">Thrown on XML, grammar, quota, routing, or deserialization errors.</exception>
        public static PreparedSoapCall ParseAndPrepare(
            ReadOnlySpan<byte> utf8Xml,
            SoapVersion expectedVersion,
            string? action,
            SoapOperationTable operations,
            SoapEndpointOptions options,
            IServiceProvider services,
            ClaimsPrincipal user)
        {
            return ParseAndPrepare(
                utf8Xml,
                expectedVersion,
                hasAction: action is not null,
                action: action.AsSpan(),
                operations,
                options,
                services,
                user);
        }

        #endregion

        #region Internal Methods

        internal static PreparedSoapCall ParseAndPrepare(
            ReadOnlySpan<byte> utf8Xml,
            SoapVersion expectedVersion,
            bool hasAction,
            ReadOnlySpan<char> action,
            SoapOperationTable operations,
            SoapEndpointOptions options,
            IServiceProvider services,
            ClaimsPrincipal user)
        {
            if (!Enum.IsDefined(expectedVersion))
            {
                throw new ArgumentOutOfRangeException(nameof(expectedVersion), expectedVersion, "Unsupported SOAP version.");
            }

            ArgumentNullException.ThrowIfNull(operations);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(user);

            if (utf8Xml.IsEmpty)
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    HttpStatusCode.BadRequest,
                    "Empty XML document.");
            }

            XmlReaderOptions readerOptions = XmlEssentialsAdapter.CreateReaderOptions(options);
            XmlEssentialsAdapter.ValidateXml10ControlBytes(utf8Xml);

            Span<byte> scratch = stackalloc byte[1024];
            Utf8XmlReader reader = new Utf8XmlReader(utf8Xml, scratch, readerOptions);

            ReadOnlySpan<byte> expectedEnvelopeNs = expectedVersion == SoapVersion.Soap11
                ? SoapConstants.Soap11NamespaceUtf8
                : SoapConstants.Soap12NamespaceUtf8;

            try
            {
                XmlEssentialsAdapter.EnsureNoProcessingInstructions(utf8Xml, readerOptions);

                if (!XmlEssentialsAdapter.ReadNextContentToken(ref reader))
                {
                    throw new SoapProtocolException(
                        SoapFaultKind.Sender,
                        HttpStatusCode.BadRequest,
                        "Empty XML document.");
                }

                if (reader.TokenType != XmlTokenType.StartElement)
                {
                    throw new SoapProtocolException(
                        SoapFaultKind.Sender,
                        HttpStatusCode.BadRequest,
                        "Expected SOAP Envelope start element.");
                }

                if (!reader.LocalNameEquals(SoapConstants.EnvelopeLocalNameUtf8))
                {
                    throw new SoapProtocolException(
                        SoapFaultKind.Sender,
                        HttpStatusCode.BadRequest,
                        "Root XML element must be SOAP Envelope.");
                }

                if (!reader.NamespaceUriEquals(expectedEnvelopeNs))
                {
                    throw new SoapProtocolException(
                        SoapFaultKind.VersionMismatch,
                        HttpStatusCode.InternalServerError,
                        "SOAP Envelope namespace does not match the endpoint SOAP version.",
                        expectedVersion);
                }

                RejectEncodingStyleAttribute(ref reader, expectedVersion, expectedEnvelopeNs);

                if (reader.IsEmptyElement)
                {
                    throw new SoapProtocolException(
                        SoapFaultKind.Sender,
                        GetSenderFaultStatusCode(expectedVersion),
                        "SOAP Envelope must contain a Body element.",
                        expectedVersion);
                }

                bool seenHeader = false;
                bool seenBody = false;
                List<SoapHeaderValue>? headers = null;
                PreparedSoapCall? preparedCall = null;

                while (XmlEssentialsAdapter.ReadNextContentToken(ref reader))
                {
                    if (reader.TokenType == XmlTokenType.EndElement)
                    {
                        break;
                    }

                    if (reader.TokenType != XmlTokenType.StartElement)
                    {
                        throw new SoapProtocolException(
                            SoapFaultKind.Sender,
                            GetSenderFaultStatusCode(expectedVersion),
                            "Unexpected content inside SOAP Envelope.",
                            expectedVersion);
                    }

                    if (!reader.NamespaceUriEquals(expectedEnvelopeNs))
                    {
                        throw new SoapProtocolException(
                            SoapFaultKind.Sender,
                            GetSenderFaultStatusCode(expectedVersion),
                            "Unexpected element namespace inside SOAP Envelope.",
                            expectedVersion);
                    }

                    if (reader.LocalNameEquals(SoapConstants.HeaderLocalNameUtf8))
                    {
                        if (seenHeader || seenBody)
                        {
                            throw new SoapProtocolException(
                                SoapFaultKind.Sender,
                                GetSenderFaultStatusCode(expectedVersion),
                                "SOAP Header must appear at most once and before Body.",
                                expectedVersion);
                        }

                        seenHeader = true;
                        RejectEncodingStyleAttribute(ref reader, expectedVersion, expectedEnvelopeNs);
                        headers = ParseHeaders(ref reader, utf8Xml, expectedVersion, expectedEnvelopeNs, options);
                        continue;
                    }

                    if (reader.LocalNameEquals(SoapConstants.BodyLocalNameUtf8))
                    {
                        if (seenBody)
                        {
                            throw new SoapProtocolException(
                                SoapFaultKind.Sender,
                                GetSenderFaultStatusCode(expectedVersion),
                                "SOAP Envelope must contain only one Body element.",
                                expectedVersion);
                        }

                        seenBody = true;
                        RejectEncodingStyleAttribute(ref reader, expectedVersion, expectedEnvelopeNs);
                        preparedCall = ParseBody(
                            ref reader,
                            expectedVersion,
                            expectedEnvelopeNs,
                            hasAction,
                            action,
                            operations,
                            headers,
                            services,
                            user);
                        continue;
                    }

                    throw new SoapProtocolException(
                        SoapFaultKind.Sender,
                        GetSenderFaultStatusCode(expectedVersion),
                        "Unexpected child element in SOAP Envelope.",
                        expectedVersion);
                }

                if (!seenBody || preparedCall is null)
                {
                    throw new SoapProtocolException(
                        SoapFaultKind.Sender,
                        GetSenderFaultStatusCode(expectedVersion),
                        "SOAP Envelope is missing a valid Body element.",
                        expectedVersion);
                }

                while (XmlEssentialsAdapter.ReadNextContentToken(ref reader))
                {
                    throw new SoapProtocolException(
                        SoapFaultKind.Sender,
                        HttpStatusCode.BadRequest,
                        "Unexpected trailing content after SOAP Envelope.");
                }

                return preparedCall;
            }
            catch (XmlException ex)
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    HttpStatusCode.BadRequest,
                    "Malformed XML in SOAP request.",
                    innerException: ex);
            }
        }

        #endregion

        #region Private Methods

        private static HttpStatusCode GetSenderFaultStatusCode(SoapVersion version)
        {
            return version == SoapVersion.Soap11
                ? HttpStatusCode.InternalServerError
                : HttpStatusCode.BadRequest;
        }

        private static PreparedSoapCall ParseBody(
            ref Utf8XmlReader reader,
            SoapVersion expectedVersion,
            ReadOnlySpan<byte> expectedEnvelopeNs,
            bool hasAction,
            ReadOnlySpan<char> action,
            SoapOperationTable operations,
            List<SoapHeaderValue>? headers,
            IServiceProvider services,
            ClaimsPrincipal user)
        {
            if (reader.IsEmptyElement)
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    GetSenderFaultStatusCode(expectedVersion),
                    "SOAP Body cannot be empty.",
                    expectedVersion);
            }

            if (!XmlEssentialsAdapter.ReadNextContentToken(ref reader) || reader.TokenType != XmlTokenType.StartElement)
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    GetSenderFaultStatusCode(expectedVersion),
                    "Expected operation element inside SOAP Body.",
                    expectedVersion);
            }

            RejectEncodingStyleAttribute(ref reader, expectedVersion, expectedEnvelopeNs);

            ISoapOperation? operation;
            if (hasAction)
            {
                if (!operations.TryGetByAction(action, out operation))
                {
                    throw new SoapProtocolException(
                        SoapFaultKind.Sender,
                        GetSenderFaultStatusCode(expectedVersion),
                        "The specified SOAP action is not supported on this endpoint.",
                        expectedVersion);
                }

                if (!operation.Descriptor.RequestElement.Matches(reader.NamespaceUriSpan, reader.LocalNameSpan))
                {
                    throw new SoapProtocolException(
                        SoapFaultKind.Sender,
                        GetSenderFaultStatusCode(expectedVersion),
                        "SOAP action does not match the Body element qualified name.",
                        expectedVersion);
                }
            }
            else
            {
                if (!operations.SupportsActionlessRouting)
                {
                    throw new SoapProtocolException(
                        SoapFaultKind.Sender,
                        GetSenderFaultStatusCode(expectedVersion),
                        "SOAP action is required on this endpoint.",
                        expectedVersion);
                }

                if (!operations.TryGetByRequestElementUtf8(reader.NamespaceUriSpan, reader.LocalNameSpan, out operation))
                {
                    throw new SoapProtocolException(
                        SoapFaultKind.Sender,
                        GetSenderFaultStatusCode(expectedVersion),
                        "Unknown SOAP operation element in Body.",
                        expectedVersion);
                }
            }

            SoapCallContext context = new SoapCallContext
            {
                Action = operation.Descriptor.Action,
                Headers = headers is null ? [] : headers,
                OperationDescriptor = operation.Descriptor,
                Services = services,
                User = user,
                Version = expectedVersion
            };

            PreparedSoapCall prepared = operation.Prepare(ref reader, context);

            if (!XmlEssentialsAdapter.ReadNextContentToken(ref reader) || reader.TokenType != XmlTokenType.EndElement)
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    GetSenderFaultStatusCode(expectedVersion),
                    "SOAP Body must contain exactly one operation element.",
                    expectedVersion);
            }

            return prepared;
        }

        private static List<SoapHeaderValue>? ParseHeaders(
            ref Utf8XmlReader reader,
            ReadOnlySpan<byte> utf8Xml,
            SoapVersion expectedVersion,
            ReadOnlySpan<byte> expectedEnvelopeNs,
            SoapEndpointOptions options)
        {
            if (reader.IsEmptyElement)
            {
                return null;
            }

            List<SoapHeaderValue>? headers = null;
            int headerCount = 0;
            int cumulativeHeaderBytes = 0;

            while (XmlEssentialsAdapter.ReadNextContentToken(ref reader))
            {
                if (reader.TokenType == XmlTokenType.EndElement)
                {
                    break;
                }

                if (reader.TokenType != XmlTokenType.StartElement)
                {
                    throw new SoapProtocolException(
                        SoapFaultKind.Sender,
                        GetSenderFaultStatusCode(expectedVersion),
                        "Unexpected content inside SOAP Header.",
                        expectedVersion);
                }

                headerCount++;
                if (headerCount > options.MaxHeaderCount)
                {
                    throw new SoapProtocolException(
                        SoapFaultKind.Sender,
                        GetSenderFaultStatusCode(expectedVersion),
                        "SOAP header count exceeded the configured limit.",
                        expectedVersion);
                }

                int startOffset = checked((int)reader.TokenStartIndex);
                bool isEmptyHeaderElement = reader.IsEmptyElement;
                int headerDepth = reader.CurrentDepth;
                string headerLocalName = Encoding.UTF8.GetString(reader.LocalNameSpan);
                string headerNamespaceUri = reader.NamespaceUriSpan.IsEmpty
                    ? string.Empty
                    : Encoding.UTF8.GetString(reader.NamespaceUriSpan);

                bool mustUnderstand = false;
                string? actorOrRole = null;

                while (reader.MoveToNextAttribute())
                {
                    if (!reader.NamespaceUriEquals(expectedEnvelopeNs))
                    {
                        continue;
                    }

                    if (reader.LocalNameEquals(SoapConstants.EncodingStyleLocalNameUtf8))
                    {
                        throw new SoapProtocolException(
                            SoapFaultKind.Sender,
                            GetSenderFaultStatusCode(expectedVersion),
                            "SOAP encodingStyle is not supported.",
                            expectedVersion);
                    }

                    if (reader.LocalNameEquals(SoapConstants.MustUnderstandLocalNameUtf8))
                    {
                        mustUnderstand = ParseMustUnderstandAttribute(ref reader, expectedVersion);
                    }
                    else if (expectedVersion == SoapVersion.Soap11 && reader.LocalNameEquals(SoapConstants.ActorLocalNameUtf8))
                    {
                        actorOrRole = ParseActorOrRoleAttribute(ref reader, expectedVersion, "SOAP 1.1 actor attribute cannot be blank.");
                    }
                    else if (expectedVersion == SoapVersion.Soap12 && reader.LocalNameEquals(SoapConstants.RoleLocalNameUtf8))
                    {
                        actorOrRole = ParseActorOrRoleAttribute(ref reader, expectedVersion, "SOAP 1.2 role attribute cannot be blank.");
                    }
                    else if (expectedVersion == SoapVersion.Soap12 && reader.LocalNameEquals(RelayLocalNameUtf8))
                    {
                        _ = ParseSoap12BooleanAttribute(ref reader, "Invalid SOAP 1.2 relay lexical value.");
                    }
                }

                reader.MoveToElement();

                if (!isEmptyHeaderElement)
                {
                    while (XmlEssentialsAdapter.ReadNextContentToken(ref reader))
                    {
                        if (reader.TokenType == XmlTokenType.EndElement && reader.CurrentDepth == headerDepth)
                        {
                            break;
                        }
                    }
                }

                int endOffset = checked((int)reader.BytesConsumed);
                int headerByteLength = checked(endOffset - startOffset);
                cumulativeHeaderBytes = checked(cumulativeHeaderBytes + headerByteLength);

                if (cumulativeHeaderBytes > options.MaxHeaderBytes)
                {
                    throw new SoapProtocolException(
                        SoapFaultKind.Sender,
                        GetSenderFaultStatusCode(expectedVersion),
                        "SOAP headers exceeded the configured byte budget.",
                        expectedVersion);
                }

                SoapQName headerQName = new SoapQName(headerNamespaceUri, headerLocalName);
                SoapHeaderValue headerValue = SoapHeaderValue.CreateCopy(
                    headerQName,
                    actorOrRole,
                    mustUnderstand,
                    utf8Xml.Slice(startOffset, headerByteLength));

                headers ??= new List<SoapHeaderValue>(4);
                headers.Add(headerValue);
            }

            return headers;
        }

        private static string ParseActorOrRoleAttribute(
            ref Utf8XmlReader reader,
            SoapVersion expectedVersion,
            string errorMessage)
        {
            string? value = reader.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    GetSenderFaultStatusCode(expectedVersion),
                    errorMessage,
                    expectedVersion);
            }

            return value;
        }

        private static bool ParseMustUnderstandAttribute(ref Utf8XmlReader reader, SoapVersion expectedVersion)
        {
            if (expectedVersion == SoapVersion.Soap11)
            {
                if (reader.ValueTextEquals("1"u8))
                {
                    return true;
                }

                if (reader.ValueTextEquals("0"u8))
                {
                    return false;
                }

                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    HttpStatusCode.InternalServerError,
                    "Invalid SOAP 1.1 mustUnderstand lexical value.",
                    SoapVersion.Soap11);
            }

            return ParseSoap12BooleanAttribute(ref reader, "Invalid SOAP 1.2 mustUnderstand lexical value.");
        }

        private static bool ParseSoap12BooleanAttribute(ref Utf8XmlReader reader, string errorMessage)
        {
            if (reader.ValueTextEquals("1"u8) || reader.ValueTextEquals("true"u8))
            {
                return true;
            }

            if (reader.ValueTextEquals("0"u8) || reader.ValueTextEquals("false"u8))
            {
                return false;
            }

            throw new SoapProtocolException(
                SoapFaultKind.Sender,
                HttpStatusCode.BadRequest,
                errorMessage,
                SoapVersion.Soap12);
        }

        private static void RejectEncodingStyleAttribute(
            ref Utf8XmlReader reader,
            SoapVersion expectedVersion,
            ReadOnlySpan<byte> expectedEnvelopeNs)
        {
            if (!reader.HasAttributes)
            {
                return;
            }

            while (reader.MoveToNextAttribute())
            {
                if (reader.NamespaceUriEquals(expectedEnvelopeNs) &&
                    reader.LocalNameEquals(SoapConstants.EncodingStyleLocalNameUtf8))
                {
                    throw new SoapProtocolException(
                        SoapFaultKind.Sender,
                        GetSenderFaultStatusCode(expectedVersion),
                        "SOAP encodingStyle is not supported.",
                        expectedVersion);
                }
            }

            reader.MoveToElement();
        }

        #endregion

    }

}
