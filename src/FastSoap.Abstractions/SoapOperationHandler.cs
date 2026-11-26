using System.Threading;
using System.Threading.Tasks;

namespace FastSoap.Abstractions
{

    /// <summary>
    /// Represents an asynchronous, reflection-free SOAP operation invocation delegate.
    /// </summary>
    /// <typeparam name="TRequest">The deserialized request wire DTO type.</typeparam>
    /// <typeparam name="TResponse">The serialized response wire DTO type.</typeparam>
    /// <param name="request">The deserialized request payload.</param>
    /// <param name="context">The per-request SOAP execution context.</param>
    /// <param name="cancellationToken">A cancellation token tied to the underlying HTTP request.</param>
    /// <returns>A <see cref="ValueTask{TResult}"/> yielding the typed response DTO.</returns>
    public delegate ValueTask<TResponse> SoapOperationHandler<TRequest, TResponse>(
        TRequest request,
        SoapCallContext context,
        CancellationToken cancellationToken);

}
