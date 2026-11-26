using System;

namespace FastSoap.Core.Buffers
{

    /// <summary>
    /// Exception thrown when an incoming SOAP request payload exceeds the configured request byte quota.
    /// </summary>
    /// <remarks>
    /// Hosting layers map this exception to HTTP <c>413 Payload Too Large</c> without invoking any SOAP operation.
    /// </remarks>
    public sealed class RequestQuotaExceededException : Exception
    {

        #region Properties

        /// <summary>
        /// Gets the configured maximum request payload size in bytes that was exceeded.
        /// </summary>
        public int MaxBytes { get; }

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of <see cref="RequestQuotaExceededException"/>.
        /// </summary>
        /// <param name="maxBytes">The configured maximum request size in bytes.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxBytes"/> is less than or equal to zero.</exception>
        public RequestQuotaExceededException(int maxBytes)
            : base($"SOAP request body exceeded the maximum limit of {maxBytes} bytes.")
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxBytes, 0);
            MaxBytes = maxBytes;
        }

        #endregion

    }

}
