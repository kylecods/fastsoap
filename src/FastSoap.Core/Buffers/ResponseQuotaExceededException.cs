using System;

namespace FastSoap.Core.Buffers
{

    /// <summary>
    /// Exception thrown when a staged SOAP response or fault envelope exceeds the configured response byte quota.
    /// </summary>
    /// <remarks>
    /// Because responses are staged prior to committing HTTP headers, quota violations prevent partial HTTP 200 writes.
    /// </remarks>
    public sealed class ResponseQuotaExceededException : Exception
    {

        #region Properties

        /// <summary>
        /// Gets the configured maximum staged response size in bytes that was exceeded.
        /// </summary>
        public int MaxBytes { get; }

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of <see cref="ResponseQuotaExceededException"/>.
        /// </summary>
        /// <param name="maxBytes">The configured maximum staged response size in bytes.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxBytes"/> is less than or equal to zero.</exception>
        public ResponseQuotaExceededException(int maxBytes)
            : base($"Staged SOAP response exceeded the maximum limit of {maxBytes} bytes.")
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxBytes, 0);
            MaxBytes = maxBytes;
        }

        #endregion

    }

}
