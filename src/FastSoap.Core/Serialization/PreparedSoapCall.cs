using System.Buffers;
using System.Threading;
using System.Threading.Tasks;

using FastSoap.Abstractions;
using FastSoap.Core.Buffers;

namespace FastSoap.Core.Serialization
{

    /// <summary>
    /// Represents a validated, deserialized SOAP operation call ready to cross the asynchronous
    /// execution and response staging boundary without boxing or reflection.
    /// </summary>
    /// <remarks>
    /// Instances hold a strongly typed deserialized request DTO and execute the typed handler before
    /// staging or streaming the full SOAP envelope.
    /// </remarks>
    public abstract class PreparedSoapCall
    {

        #region Properties

        /// <summary>
        /// Gets the per-request execution context associated with this prepared call.
        /// </summary>
        public abstract SoapCallContext Context { get; }

        /// <summary>
        /// Gets the matched immutable descriptor for the target SOAP operation.
        /// </summary>
        public abstract SoapOperationDescriptor Descriptor { get; }

        internal virtual bool SupportsSplitExecution => false;

        #endregion

        #region Public Methods

        /// <summary>
        /// Executes the typed operation handler and stages the complete SOAP envelope into <paramref name="destination"/>.
        /// </summary>
        /// <param name="destination">The bounded staging writer receiving the complete SOAP response envelope.</param>
        /// <param name="cancellationToken">A cancellation token tied to the request.</param>
        /// <returns>A <see cref="ValueTask"/> representing the asynchronous execution and response staging.</returns>
        public abstract ValueTask ExecuteAndWriteAsync(
            BoundedPooledBufferWriter destination,
            CancellationToken cancellationToken);

        #endregion

        #region Internal Methods

        internal virtual ValueTask ExecuteAsync(CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        internal virtual long WriteDirectResponse(IBufferWriter<byte> destination)
        {
            return 0;
        }

        #endregion

    }

}
