using System;

namespace FastSoap.Abstractions
{

    /// <summary>
    /// Represents a reflection-free, WCF-compatible <c>ExceptionDetail</c> data transfer object matching
    /// the <c>http://schemas.datacontract.org/2004/07/System.ServiceModel</c> wire schema.
    /// </summary>
    /// <example>
    /// <code>
    /// SoapExceptionDetail detail = SoapExceptionDetail.FromException(exception, includeStackTrace: false);
    /// </code>
    /// </example>
    /// <remarks>
    /// Use this type only for explicitly opt-in diagnostics or migrated WCF clients expecting
    /// <c>FaultException&lt;ExceptionDetail&gt;</c>; unhandled internal exceptions remain sanitized by default.
    /// </remarks>
    public sealed class SoapExceptionDetail
    {

        #region Fields

        /// <summary>
        /// Canonical WCF <c>ExceptionDetail</c> XML DataContract namespace URI.
        /// </summary>
        public const string WcfNamespace = "http://schemas.datacontract.org/2004/07/System.ServiceModel";

        private const int MaxInnerExceptionDepth = 16;

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the optional help link URI associated with the exception.
        /// </summary>
        public string? HelpLink { get; set; }

        /// <summary>
        /// Gets or sets the optional inner <see cref="SoapExceptionDetail"/> entry.
        /// </summary>
        public SoapExceptionDetail? InnerException { get; set; }

        /// <summary>
        /// Gets or sets the exception message text.
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// Gets or sets the optional stack trace string.
        /// </summary>
        public string? StackTrace { get; set; }

        /// <summary>
        /// Gets or sets the full CLR type name of the exception.
        /// </summary>
        public string? Type { get; set; }

        #endregion

        #region Public Methods

        /// <summary>
        /// Creates a <see cref="SoapExceptionDetail"/> snapshot from <paramref name="exception"/> without reflection.
        /// </summary>
        /// <param name="exception">The source <see cref="Exception"/> to project.</param>
        /// <param name="includeStackTrace">Whether to copy <see cref="Exception.StackTrace"/>; defaults to <see langword="false"/>.</param>
        /// <returns>A populated <see cref="SoapExceptionDetail"/> instance.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="exception"/> is <see langword="null"/>.</exception>
        public static SoapExceptionDetail FromException(Exception exception, bool includeStackTrace = false)
        {
            ArgumentNullException.ThrowIfNull(exception);

            return FromExceptionCore(exception, includeStackTrace, depth: 0);
        }

        #endregion

        #region Private Methods

        private static SoapExceptionDetail FromExceptionCore(Exception exception, bool includeStackTrace, int depth)
        {
            SoapExceptionDetail? inner = exception.InnerException is not null && depth + 1 < MaxInnerExceptionDepth
                ? FromExceptionCore(exception.InnerException, includeStackTrace, depth + 1)
                : null;

            return new SoapExceptionDetail
            {
                HelpLink = exception.HelpLink,
                InnerException = inner,
                Message = exception.Message,
                StackTrace = includeStackTrace ? exception.StackTrace : null,
                Type = exception.GetType().FullName
            };
        }

        #endregion

    }

}
