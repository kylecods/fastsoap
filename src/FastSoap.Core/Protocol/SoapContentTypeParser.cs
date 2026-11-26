using System;
using System.Buffers;
using System.Net;
using System.Text;
using System.Text.Unicode;

using FastSoap.Abstractions;

namespace FastSoap.Core.Protocol
{

    /// <summary>
    /// Represents parsed SOAP HTTP <c>Content-Type</c> metadata including negotiated protocol version and
    /// optional SOAP 1.2 <c>action</c> parameter.
    /// </summary>
    public readonly record struct SoapContentTypeInfo(SoapVersion Version, string? Action);

    /// <summary>
    /// Parses and validates HTTP <c>Content-Type</c> headers for SOAP 1.1 and SOAP 1.2 endpoints
    /// directly in UTF-8 without string-splitting allocations.
    /// </summary>
    /// <remarks>
    /// Rejects non-UTF-8 charsets, disallowed SOAP protocol versions, malformed RFC 9110 parameter syntax,
    /// and duplicate <c>charset</c> or <c>action</c> parameters.
    /// </remarks>
    public static class SoapContentTypeParser
    {

        #region Fields

        private static ReadOnlySpan<byte> ActionParameterNameUtf8 => "action"u8;
        private static ReadOnlySpan<byte> CharsetParameterNameUtf8 => "charset"u8;
        private static ReadOnlySpan<byte> Utf8CharsetUtf8 => "utf-8"u8;

        #endregion

        #region Public Methods

        /// <summary>
        /// Parses a <see cref="string"/> HTTP <c>Content-Type</c> header value against <paramref name="enabledVersions"/>.
        /// </summary>
        /// <param name="contentType">The raw HTTP <c>Content-Type</c> header value.</param>
        /// <param name="enabledVersions">The SOAP protocol versions enabled on the target endpoint.</param>
        /// <param name="maxActionLength">The maximum permitted character length for a SOAP 1.2 <c>action</c> parameter.</param>
        /// <returns>The validated <see cref="SoapContentTypeInfo"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="enabledVersions"/> or <paramref name="maxActionLength"/> is invalid.</exception>
        /// <exception cref="SoapProtocolException">Thrown with HTTP 415 or 400 when media type, charset, or parameters are invalid.</exception>
        public static SoapContentTypeInfo Parse(
            string? contentType,
            SoapVersionFlags enabledVersions,
            int maxActionLength = 1024)
        {
            ValidateArguments(enabledVersions, maxActionLength);

            if (string.IsNullOrWhiteSpace(contentType))
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    HttpStatusCode.UnsupportedMediaType,
                    "Missing Content-Type header.");
            }

            int maxByteCount = Encoding.UTF8.GetMaxByteCount(contentType.Length);
            if (maxByteCount <= 512)
            {
                Span<byte> stackBytes = stackalloc byte[512];
                int written = Encoding.UTF8.GetBytes(contentType, stackBytes);

                return ParseCore(stackBytes[..written], enabledVersions, maxActionLength);
            }

            byte[] heapBytes = Encoding.UTF8.GetBytes(contentType);

            return ParseCore(heapBytes, enabledVersions, maxActionLength);
        }

        /// <summary>
        /// Parses a UTF-8 HTTP <c>Content-Type</c> header value against <paramref name="enabledVersions"/>.
        /// </summary>
        /// <param name="contentTypeUtf8">The raw UTF-8 encoded <c>Content-Type</c> header bytes.</param>
        /// <param name="enabledVersions">The SOAP protocol versions enabled on the target endpoint.</param>
        /// <param name="maxActionLength">The maximum permitted character length for a SOAP 1.2 <c>action</c> parameter.</param>
        /// <returns>The validated <see cref="SoapContentTypeInfo"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="enabledVersions"/> or <paramref name="maxActionLength"/> is invalid.</exception>
        /// <exception cref="SoapProtocolException">Thrown with HTTP 415 or 400 when media type, charset, or parameters are invalid.</exception>
        public static SoapContentTypeInfo Parse(
            ReadOnlySpan<byte> contentTypeUtf8,
            SoapVersionFlags enabledVersions,
            int maxActionLength = 1024)
        {
            ValidateArguments(enabledVersions, maxActionLength);

            return ParseCore(contentTypeUtf8, enabledVersions, maxActionLength);
        }

        #endregion

        #region Private Methods

        private static bool IsTokenChar(byte value)
        {
            if (value <= 0x20 || value >= 0x7F)
            {
                return false;
            }

            return value switch
            {
                (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'@' or
                (byte)',' or (byte)';' or (byte)':' or (byte)'\\' or (byte)'"' or
                (byte)'/' or (byte)'[' or (byte)']' or (byte)'?' or (byte)'=' or
                (byte)'{' or (byte)'}' => false,
                _ => true
            };
        }

        private static bool IsValidMediaTypeToken(ReadOnlySpan<byte> mediaType)
        {
            int slashIndex = mediaType.IndexOf((byte)'/');
            if (slashIndex <= 0 || slashIndex == mediaType.Length - 1)
            {
                return false;
            }

            ReadOnlySpan<byte> type = mediaType[..slashIndex];
            ReadOnlySpan<byte> subtype = mediaType[(slashIndex + 1)..];

            return IsValidToken(type) && IsValidToken(subtype);
        }

        private static bool IsValidToken(ReadOnlySpan<byte> span)
        {
            if (span.IsEmpty)
            {
                return false;
            }

            for (int index = 0; index < span.Length; index++)
            {
                if (!IsTokenChar(span[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsWhiteSpace(byte value)
        {
            return value == (byte)' ' || value == (byte)'\t';
        }

        private static SoapContentTypeInfo ParseCore(
            ReadOnlySpan<byte> contentTypeUtf8,
            SoapVersionFlags enabledVersions,
            int maxActionLength)
        {
            if (!Utf8.IsValid(contentTypeUtf8))
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    HttpStatusCode.BadRequest,
                    "Malformed Content-Type header.");
            }

            ReadOnlySpan<byte> trimmed = TrimOws(contentTypeUtf8);
            if (trimmed.IsEmpty)
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    HttpStatusCode.UnsupportedMediaType,
                    "Missing Content-Type header.");
            }

            int semicolonIndex = trimmed.IndexOf((byte)';');
            ReadOnlySpan<byte> mediaTypePart = semicolonIndex < 0
                ? trimmed
                : TrimOws(trimmed[..semicolonIndex]);

            if (!IsValidMediaTypeToken(mediaTypePart))
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    HttpStatusCode.UnsupportedMediaType,
                    "Invalid Content-Type media type.");
            }

            SoapVersion version;
            if (Ascii.EqualsIgnoreCase(mediaTypePart, SoapConstants.Soap11MediaTypeUtf8))
            {
                version = SoapVersion.Soap11;
            }
            else if (Ascii.EqualsIgnoreCase(mediaTypePart, SoapConstants.Soap12MediaTypeUtf8))
            {
                version = SoapVersion.Soap12;
            }
            else
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    HttpStatusCode.UnsupportedMediaType,
                    "Unsupported Content-Type media type.");
            }

            bool isVersionEnabled = version == SoapVersion.Soap11
                ? (enabledVersions & SoapVersionFlags.Soap11) != 0
                : (enabledVersions & SoapVersionFlags.Soap12) != 0;

            if (!isVersionEnabled)
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    HttpStatusCode.UnsupportedMediaType,
                    "The requested SOAP protocol version is not enabled on this endpoint.");
            }

            if (semicolonIndex < 0)
            {
                return new SoapContentTypeInfo(version, null);
            }

            ReadOnlySpan<byte> remaining = trimmed[(semicolonIndex + 1)..];
            if (remaining.IsEmpty)
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    HttpStatusCode.BadRequest,
                    "Malformed trailing semicolon in Content-Type header.");
            }

            bool seenCharset = false;
            bool seenAction = false;
            string? action = null;
            Span<byte> stackDecodedBuffer = stackalloc byte[512];

            while (!remaining.IsEmpty)
            {
                remaining = TrimLeadingOws(remaining);
                if (remaining.IsEmpty)
                {
                    throw new SoapProtocolException(
                        SoapFaultKind.Sender,
                        HttpStatusCode.BadRequest,
                        "Malformed trailing semicolon in Content-Type header.");
                }

                int equalsIndex = remaining.IndexOf((byte)'=');
                if (equalsIndex <= 0)
                {
                    throw new SoapProtocolException(
                        SoapFaultKind.Sender,
                        HttpStatusCode.BadRequest,
                        "Malformed parameter in Content-Type header.");
                }

                ReadOnlySpan<byte> paramName = remaining[..equalsIndex];
                if (!IsValidToken(paramName))
                {
                    throw new SoapProtocolException(
                        SoapFaultKind.Sender,
                        HttpStatusCode.BadRequest,
                        "Invalid parameter name in Content-Type header.");
                }

                ReadOnlySpan<byte> valueSection = remaining[(equalsIndex + 1)..];
                if (valueSection.IsEmpty)
                {
                    throw new SoapProtocolException(
                        SoapFaultKind.Sender,
                        HttpStatusCode.BadRequest,
                        "Missing parameter value in Content-Type header.");
                }

                int consumed;
                bool isCharset = Ascii.EqualsIgnoreCase(paramName, CharsetParameterNameUtf8);
                bool isSoap12Action = version == SoapVersion.Soap12 && Ascii.EqualsIgnoreCase(paramName, ActionParameterNameUtf8);

                if (valueSection[0] == (byte)'"')
                {
                    byte[]? rented = null;
                    Span<byte> decodedBuffer = valueSection.Length <= stackDecodedBuffer.Length
                        ? stackDecodedBuffer
                        : (rented = ArrayPool<byte>.Shared.Rent(valueSection.Length));

                    try
                    {
                        int decodedLength = ParseQuotedParameterBytes(valueSection, decodedBuffer, out consumed);
                        remaining = ConsumeDelimiterAndAdvance(valueSection, consumed);

                        ReadOnlySpan<byte> decodedSpan = decodedBuffer[..decodedLength];
                        if (isCharset)
                        {
                            ValidateCharsetUtf8(decodedSpan, ref seenCharset);
                        }
                        else if (isSoap12Action)
                        {
                            action = DecodeAndValidateSoap12Action(decodedSpan, maxActionLength, ref seenAction);
                        }
                    }
                    finally
                    {
                        if (rented is not null)
                        {
                            ArrayPool<byte>.Shared.Return(rented);
                        }
                    }
                }
                else
                {
                    ReadOnlySpan<byte> tokenBytes = ParseTokenParameterBytes(valueSection, out consumed);
                    remaining = ConsumeDelimiterAndAdvance(valueSection, consumed);

                    if (isCharset)
                    {
                        ValidateCharsetUtf8(tokenBytes, ref seenCharset);
                    }
                    else if (isSoap12Action)
                    {
                        action = DecodeAndValidateSoap12Action(tokenBytes, maxActionLength, ref seenAction);
                    }
                }
            }

            return new SoapContentTypeInfo(version, action);
        }

        private static ReadOnlySpan<byte> ConsumeDelimiterAndAdvance(ReadOnlySpan<byte> valueSection, int consumed)
        {
            ReadOnlySpan<byte> afterValue = TrimLeadingOws(valueSection[consumed..]);
            if (!afterValue.IsEmpty)
            {
                if (afterValue[0] != (byte)';')
                {
                    throw new SoapProtocolException(
                        SoapFaultKind.Sender,
                        HttpStatusCode.BadRequest,
                        "Malformed parameter delimiter in Content-Type header.");
                }

                afterValue = afterValue[1..];
                if (afterValue.IsEmpty)
                {
                    throw new SoapProtocolException(
                        SoapFaultKind.Sender,
                        HttpStatusCode.BadRequest,
                        "Malformed trailing semicolon in Content-Type header.");
                }
            }

            return afterValue;
        }

        private static string DecodeAndValidateSoap12Action(
            ReadOnlySpan<byte> actionUtf8,
            int maxActionLength,
            ref bool seenAction)
        {
            if (seenAction)
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    HttpStatusCode.BadRequest,
                    "Duplicate action parameter in Content-Type header.");
            }

            seenAction = true;

            if (actionUtf8.IsEmpty)
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    HttpStatusCode.BadRequest,
                    "SOAP 1.2 action parameter cannot be empty.");
            }

            string decoded = Encoding.UTF8.GetString(actionUtf8);
            if (string.IsNullOrWhiteSpace(decoded))
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    HttpStatusCode.BadRequest,
                    "SOAP 1.2 action parameter cannot be empty.");
            }

            if (decoded.Length > maxActionLength)
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    HttpStatusCode.BadRequest,
                    "SOAP 1.2 action parameter exceeds the maximum allowed length.");
            }

            return decoded;
        }

        private static int ParseQuotedParameterBytes(
            ReadOnlySpan<byte> valueSection,
            Span<byte> destination,
            out int consumed)
        {
            int decodedLength = 0;
            bool closed = false;
            int index = 1;

            while (index < valueSection.Length)
            {
                byte current = valueSection[index];
                if (current == (byte)'\0' || current == (byte)'\r' || current == (byte)'\n')
                {
                    throw new SoapProtocolException(
                        SoapFaultKind.Sender,
                        HttpStatusCode.BadRequest,
                        "Control characters are not allowed in Content-Type parameters.");
                }

                if (current == (byte)'"')
                {
                    closed = true;
                    index++;
                    break;
                }

                if (current == (byte)'\\')
                {
                    index++;
                    if (index >= valueSection.Length)
                    {
                        throw new SoapProtocolException(
                            SoapFaultKind.Sender,
                            HttpStatusCode.BadRequest,
                            "Unterminated escape sequence in Content-Type parameter.");
                    }

                    byte escaped = valueSection[index];
                    if (escaped == (byte)'\0' || escaped == (byte)'\r' || escaped == (byte)'\n')
                    {
                        throw new SoapProtocolException(
                            SoapFaultKind.Sender,
                            HttpStatusCode.BadRequest,
                            "Control characters are not allowed in Content-Type parameters.");
                    }

                    destination[decodedLength++] = escaped;
                    index++;
                    continue;
                }

                destination[decodedLength++] = current;
                index++;
            }

            if (!closed)
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    HttpStatusCode.BadRequest,
                    "Unterminated quoted Content-Type parameter.");
            }

            consumed = index;

            return decodedLength;
        }

        private static ReadOnlySpan<byte> ParseTokenParameterBytes(ReadOnlySpan<byte> valueSection, out int consumed)
        {
            int length = 0;
            while (length < valueSection.Length && IsTokenChar(valueSection[length]))
            {
                length++;
            }

            if (length == 0)
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    HttpStatusCode.BadRequest,
                    "Invalid parameter value in Content-Type header.");
            }

            consumed = length;

            return valueSection[..length];
        }

        private static void ValidateCharsetUtf8(ReadOnlySpan<byte> charsetUtf8, ref bool seenCharset)
        {
            if (seenCharset)
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    HttpStatusCode.BadRequest,
                    "Duplicate charset parameter in Content-Type header.");
            }

            seenCharset = true;
            if (!Ascii.EqualsIgnoreCase(charsetUtf8, Utf8CharsetUtf8))
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    HttpStatusCode.UnsupportedMediaType,
                    "Only UTF-8 character encoding is supported.");
            }
        }

        private static ReadOnlySpan<byte> TrimLeadingOws(ReadOnlySpan<byte> span)
        {
            int start = 0;
            while (start < span.Length && IsWhiteSpace(span[start]))
            {
                start++;
            }

            return span[start..];
        }

        private static ReadOnlySpan<byte> TrimOws(ReadOnlySpan<byte> span)
        {
            int start = 0;
            while (start < span.Length && IsWhiteSpace(span[start]))
            {
                start++;
            }

            int end = span.Length;
            while (end > start && IsWhiteSpace(span[end - 1]))
            {
                end--;
            }

            return span[start..end];
        }

        private static void ValidateArguments(SoapVersionFlags enabledVersions, int maxActionLength)
        {
            if (enabledVersions == SoapVersionFlags.None || (enabledVersions & ~SoapVersionFlags.Both) != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(enabledVersions),
                    enabledVersions,
                    "At least one valid SOAP protocol version flag must be enabled.");
            }

            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxActionLength, 0);
        }

        #endregion

    }

}
