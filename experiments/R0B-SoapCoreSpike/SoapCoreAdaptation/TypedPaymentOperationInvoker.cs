using System;
using System.Reflection;
using System.Threading.Tasks;
using R0B.SoapCoreSpike.Contracts;
using SoapCore.Extensibility;

namespace R0B.SoapCoreSpike.SoapCoreAdaptation
{
    /// <summary>
    /// Replaces SoapCore's reflection-based <c>DefaultOperationInvoker</c> (<c>MethodInfo.Invoke</c> +
    /// <c>Task&lt;T&gt;.Result</c> reflection) with static typed dispatch for <see cref="IPaymentService"/>.
    /// </summary>
    /// <remarks>
    /// Even when this invoker avoids <c>MethodInfo.Invoke</c>, SoapCore's <c>SoapEndpointMiddleware</c>
    /// still reflects over <c>ServiceType</c> at startup via <c>ServiceDescription</c> and still passes
    /// <c>MethodInfo</c> + boxed <c>object[]</c> arguments per call.
    /// </remarks>
    public sealed class TypedPaymentOperationInvoker : IOperationInvoker
    {
        #region Public Methods

        /// <inheritdoc />
        public async Task<object?> InvokeAsync(MethodInfo methodInfo, object instance, object[] inputs)
        {
            ArgumentNullException.ThrowIfNull(methodInfo);
            ArgumentNullException.ThrowIfNull(instance);
            ArgumentNullException.ThrowIfNull(inputs);

            if (instance is IPaymentService paymentService)
            {
                if (string.Equals(methodInfo.Name, nameof(IPaymentService.EchoAsync), StringComparison.Ordinal))
                {
                    string message = (string)inputs[0];
                    string echoResult = await paymentService.EchoAsync(message).ConfigureAwait(false);

                    return echoResult;
                }

                if (string.Equals(methodInfo.Name, nameof(IPaymentService.TransferAsync), StringComparison.Ordinal))
                {
                    TransferRequest request = (TransferRequest)inputs[0];
                    TransferResult transferResult = await paymentService.TransferAsync(request).ConfigureAwait(false);

                    return transferResult;
                }
            }

            throw new NotSupportedException(
                $"Static operation invoker does not support {methodInfo.DeclaringType?.FullName}.{methodInfo.Name}.");
        }

        #endregion
    }
}
