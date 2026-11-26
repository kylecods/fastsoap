using System.Threading;
using System.Threading.Tasks;

namespace FastSoap.Abstractions
{

    /// <summary>
    /// Defines an explicitly registered handler for a SOAP header identified by expanded qualified name.
    /// </summary>
    /// <remarks>
    /// Header handlers run after full synchronous envelope parsing and before service operation invocation.
    /// </remarks>
    public interface ISoapHeaderHandler
    {

        #region Properties

        /// <summary>
        /// Gets the expanded qualified name of the SOAP header handled by this instance.
        /// </summary>
        SoapQName HeaderName { get; }

        #endregion

        #region Public Methods

        /// <summary>
        /// Validates or processes a parsed SOAP header block prior to invoking the target operation.
        /// </summary>
        /// <param name="header">The owned immutable SOAP header value.</param>
        /// <param name="context">The current SOAP call context.</param>
        /// <param name="cancellationToken">A cancellation token tied to the HTTP request.</param>
        /// <returns>A <see cref="ValueTask"/> representing the asynchronous header processing operation.</returns>
        ValueTask HandleAsync(
            SoapHeaderValue header,
            SoapCallContext context,
            CancellationToken cancellationToken);

        #endregion

    }

}
