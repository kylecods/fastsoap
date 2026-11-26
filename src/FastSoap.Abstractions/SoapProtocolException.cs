using System;
using System.Net;

namespace FastSoap.Abstractions
{

    /// <summary>
    /// Represents a typed SOAP protocol or transport boundary failure carrying a safe public fault mapping.
    /// </summary>
    /// <remarks>
    /// <see cref="PublicReason"/> is safe to emit to clients; internal diagnostics belong in <see cref="Exception.InnerException"/>
    /// and server logs, never on the wire.
    /// </remarks>
    public sealed class SoapProtocolException : Exception
    {

        #region Properties

        /// <summary>
        /// Gets the normalized SOAP fault classification for this failure.
        /// </summary>
        public SoapFaultKind FaultKind { get; }

        /// <summary>
        /// Gets the HTTP status code mapped to this failure.
        /// </summary>
        public HttpStatusCode HttpStatusCode { get; }

        /// <summary>
        /// Gets the safe, public reason string suitable for wire fault responses.
        /// </summary>
        public string PublicReason { get; }

        /// <summary>
        /// Gets the detected SOAP protocol version if established before the failure occurred; otherwise <see langword="null"/>.
        /// </summary>
        public SoapVersion? SoapVersion { get; }

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new validated <see cref="SoapProtocolException"/>.
        /// </summary>
        /// <param name="faultKind">Normalized SOAP fault classification.</param>
        /// <param name="httpStatusCode">HTTP error status code (400-599).</param>
        /// <param name="publicReason">Safe public fault reason string.</param>
        /// <param name="soapVersion">Optional established SOAP version.</param>
        /// <param name="innerException">Optional underlying exception for server-side diagnostics.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="faultKind"/>, <paramref name="httpStatusCode"/>, or <paramref name="soapVersion"/> is invalid.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="publicReason"/> is null, empty, or whitespace.</exception>
        public SoapProtocolException(
            SoapFaultKind faultKind,
            HttpStatusCode httpStatusCode,
            string publicReason,
            SoapVersion? soapVersion = null,
            Exception? innerException = null)
            : base(ValidatePublicReason(publicReason), innerException)
        {
            if (!Enum.IsDefined(faultKind))
            {
                throw new ArgumentOutOfRangeException(nameof(faultKind), faultKind, "Unsupported SOAP fault kind.");
            }

            int statusCode = (int)httpStatusCode;
            if (statusCode < 400 || statusCode > 599)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(httpStatusCode),
                    httpStatusCode,
                    "HTTP status code for a SOAP protocol failure must be between 400 and 599.");
            }

            if (soapVersion is SoapVersion version && !Enum.IsDefined(version))
            {
                throw new ArgumentOutOfRangeException(nameof(soapVersion), soapVersion, "Unsupported SOAP version.");
            }

            FaultKind = faultKind;
            HttpStatusCode = httpStatusCode;
            PublicReason = publicReason;
            SoapVersion = soapVersion;
        }

        #endregion

        #region Private Methods

        private static string ValidatePublicReason(string publicReason)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(publicReason);

            return publicReason;
        }

        #endregion

    }

}
