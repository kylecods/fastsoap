using System;
using System.Buffers;
using System.Net;
using System.Text;
using System.Text.Unicode;

using FastSoap.Abstractions;

namespace FastSoap.Core.Protocol
{

    /// <summary>
    /// Validates and extracts SOAP 1.1 <c>SOAPAction</c> HTTP headers under strict quoting and length quotas.
    /// </summary>
    /// <remarks>
    /// Missing or empty <c>SOAPAction</c> values produce a SOAP 1.1 <c>Client</c> fault unless
    /// <c>allowMissingSoap11Action</c> is enabled, whereas malformed or oversized headers return HTTP <c>400</c>.
    /// </remarks>
    public static class SoapActionParser
    {

        #region Fields

        private static readonly SearchValues<char> ForbiddenChars = SearchValues.Create("\0\r\n\"");
        private static readonly SearchValues<byte> ForbiddenUtf8Chars = SearchValues.Create("\0\r\n\""u8);

        #endregion

        #region Public Methods

        /// <summary>
        /// Parses a SOAP 1.1 <c>SOAPAction</c> header string.
        /// </summary>
        /// <param name="soapActionHeader">The raw <c>SOAPAction</c> HTTP header value, or <see langword="null"/> if absent.</param>
        /// <param name="hasMultipleHeaders">Whether multiple <c>SOAPAction</c> headers were present on the HTTP request.</param>
        /// <param name="allowMissingSoap11Action">Whether missing or empty <c>SOAPAction</c> headers may fall back to Body QName routing.</param>
        /// <param name="maxActionLength">The maximum permitted action length in characters.</param>
        /// <returns>The unquoted action string, or <see langword="null"/> when missing/empty and fallback is allowed.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxActionLength"/> is less than or equal to zero.</exception>
        /// <exception cref="SoapProtocolException">Thrown when the header is missing (strict), malformed, duplicated, or oversized.</exception>
        public static string? ParseSoap11Action(
            string? soapActionHeader,
            bool hasMultipleHeaders,
            bool allowMissingSoap11Action,
            int maxActionLength = 1024)
        {
            ReadOnlySpan<char> actionSpan = ParseSoap11ActionSpan(
                soapActionHeader,
                hasMultipleHeaders,
                allowMissingSoap11Action,
                maxActionLength,
                out bool hasAction);

            return hasAction ? actionSpan.ToString() : null;
        }

        /// <summary>
        /// Parses a UTF-8 encoded SOAP 1.1 <c>SOAPAction</c> header value.
        /// </summary>
        /// <param name="soapActionHeaderUtf8">The raw UTF-8 bytes of the <c>SOAPAction</c> header.</param>
        /// <param name="isPresent">Whether the <c>SOAPAction</c> header was present on the request.</param>
        /// <param name="hasMultipleHeaders">Whether multiple <c>SOAPAction</c> headers were present on the HTTP request.</param>
        /// <param name="allowMissingSoap11Action">Whether missing or empty <c>SOAPAction</c> headers may fall back to Body QName routing.</param>
        /// <param name="maxActionLength">The maximum permitted action length in characters.</param>
        /// <returns>The unquoted action string, or <see langword="null"/> when missing/empty and fallback is allowed.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxActionLength"/> is less than or equal to zero.</exception>
        /// <exception cref="SoapProtocolException">Thrown when the header is missing (strict), malformed, duplicated, or oversized.</exception>
        public static string? ParseSoap11Action(
            ReadOnlySpan<byte> soapActionHeaderUtf8,
            bool isPresent,
            bool hasMultipleHeaders,
            bool allowMissingSoap11Action,
            int maxActionLength = 1024)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxActionLength, 0);

            if (hasMultipleHeaders)
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    HttpStatusCode.BadRequest,
                    "Multiple SOAPAction headers are not allowed.");
            }

            if (!isPresent)
            {
                return HandleMissingOrEmptyAction(allowMissingSoap11Action, "Missing required SOAPAction header.");
            }

            return ParseSoap11ActionCore(soapActionHeaderUtf8, allowMissingSoap11Action, maxActionLength);
        }

        #endregion

        #region Internal Methods

        internal static ReadOnlySpan<char> ParseSoap11ActionSpan(
            string? soapActionHeader,
            bool hasMultipleHeaders,
            bool allowMissingSoap11Action,
            int maxActionLength,
            out bool hasAction)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxActionLength, 0);

            if (hasMultipleHeaders)
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    HttpStatusCode.BadRequest,
                    "Multiple SOAPAction headers are not allowed.");
            }

            if (soapActionHeader is null)
            {
                _ = HandleMissingOrEmptyAction(allowMissingSoap11Action, "Missing required SOAPAction header.");
                hasAction = false;

                return ReadOnlySpan<char>.Empty;
            }

            ReadOnlySpan<char> trimmed = TrimOwsChars(soapActionHeader.AsSpan());
            if (trimmed.IsEmpty)
            {
                _ = HandleMissingOrEmptyAction(allowMissingSoap11Action, "Empty SOAPAction header is not allowed.");
                hasAction = false;

                return ReadOnlySpan<char>.Empty;
            }

            if (trimmed.Length < 2 || trimmed[0] != '"' || trimmed[^1] != '"')
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    HttpStatusCode.BadRequest,
                    "SOAPAction header must be enclosed in double quotes.");
            }

            ReadOnlySpan<char> inner = trimmed[1..^1];
            if (inner.IsEmpty)
            {
                _ = HandleMissingOrEmptyAction(allowMissingSoap11Action, "Empty SOAPAction header is not allowed.");
                hasAction = false;

                return ReadOnlySpan<char>.Empty;
            }

            if (inner.ContainsAny(ForbiddenChars))
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    HttpStatusCode.BadRequest,
                    "Malformed SOAPAction header value.");
            }

            if (inner.IsWhiteSpace())
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    HttpStatusCode.BadRequest,
                    "SOAPAction header cannot be whitespace.");
            }

            if (inner.Length > maxActionLength)
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    HttpStatusCode.BadRequest,
                    "SOAPAction header exceeds the maximum allowed length.");
            }

            hasAction = true;

            return inner;
        }

        #endregion

        #region Private Methods

        private static string? HandleMissingOrEmptyAction(bool allowMissingSoap11Action, string reason)
        {
            if (allowMissingSoap11Action)
            {
                return null;
            }

            throw new SoapProtocolException(
                SoapFaultKind.Sender,
                HttpStatusCode.InternalServerError,
                reason,
                SoapVersion.Soap11);
        }

        private static string? ParseSoap11ActionCore(
            ReadOnlySpan<byte> soapActionHeaderUtf8,
            bool allowMissingSoap11Action,
            int maxActionLength)
        {
            if (!Utf8.IsValid(soapActionHeaderUtf8))
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    HttpStatusCode.BadRequest,
                    "Malformed SOAPAction header.");
            }

            ReadOnlySpan<byte> trimmed = TrimOws(soapActionHeaderUtf8);
            if (trimmed.IsEmpty)
            {
                return HandleMissingOrEmptyAction(allowMissingSoap11Action, "Empty SOAPAction header is not allowed.");
            }

            if (trimmed.Length < 2 || trimmed[0] != (byte)'"' || trimmed[^1] != (byte)'"')
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    HttpStatusCode.BadRequest,
                    "SOAPAction header must be enclosed in double quotes.");
            }

            ReadOnlySpan<byte> innerUtf8 = trimmed[1..^1];
            if (innerUtf8.IsEmpty)
            {
                return HandleMissingOrEmptyAction(allowMissingSoap11Action, "Empty SOAPAction header is not allowed.");
            }

            if (innerUtf8.ContainsAny(ForbiddenUtf8Chars))
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    HttpStatusCode.BadRequest,
                    "Malformed SOAPAction header value.");
            }

            string action = Encoding.UTF8.GetString(innerUtf8);
            if (string.IsNullOrWhiteSpace(action))
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    HttpStatusCode.BadRequest,
                    "SOAPAction header cannot be whitespace.");
            }

            if (action.Length > maxActionLength)
            {
                throw new SoapProtocolException(
                    SoapFaultKind.Sender,
                    HttpStatusCode.BadRequest,
                    "SOAPAction header exceeds the maximum allowed length.");
            }

            return action;
        }

        private static ReadOnlySpan<byte> TrimOws(ReadOnlySpan<byte> span)
        {
            int start = 0;
            while (start < span.Length && (span[start] == (byte)' ' || span[start] == (byte)'\t'))
            {
                start++;
            }

            int end = span.Length;
            while (end > start && (span[end - 1] == (byte)' ' || span[end - 1] == (byte)'\t'))
            {
                end--;
            }

            return span[start..end];
        }

        private static ReadOnlySpan<char> TrimOwsChars(ReadOnlySpan<char> span)
        {
            int start = 0;
            while (start < span.Length && (span[start] == ' ' || span[start] == '\t'))
            {
                start++;
            }

            int end = span.Length;
            while (end > start && (span[end - 1] == ' ' || span[end - 1] == '\t'))
            {
                end--;
            }

            return span[start..end];
        }

        #endregion

    }

}
