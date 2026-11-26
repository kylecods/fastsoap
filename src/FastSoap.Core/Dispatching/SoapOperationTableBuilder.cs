using System;
using System.Collections.Frozen;
using System.Collections.Generic;

using CloudNimble.Text.Xml.Serialization;

using FastSoap.Abstractions;
using FastSoap.Core.Serialization;

namespace FastSoap.Core.Dispatching
{

    /// <summary>
    /// Validates registered SOAP operations and builds an immutable <see cref="SoapOperationTable"/>.
    /// </summary>
    /// <remarks>
    /// Rejects duplicate actions and invalid descriptors immediately, and rejects ambiguous request Body
    /// qualified names when missing-action or SOAP 1.2 routing is enabled.
    /// </remarks>
    public sealed class SoapOperationTableBuilder
    {

        #region Fields

        private readonly Dictionary<string, ISoapOperation> _operationsByAction = new Dictionary<string, ISoapOperation>(StringComparer.Ordinal);

        #endregion

        #region Public Methods

        /// <summary>
        /// Adds a pre-constructed <see cref="ISoapOperation"/> to the builder.
        /// </summary>
        /// <param name="operation">The SOAP operation to register.</param>
        /// <returns>This builder instance for fluent chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when <see cref="ISoapOperation.Descriptor"/> is missing.</exception>
        /// <exception cref="InvalidOperationException">Thrown when an operation with the same <see cref="SoapOperationDescriptor.Action"/> is already registered.</exception>
        public SoapOperationTableBuilder AddOperation(ISoapOperation operation)
        {
            ArgumentNullException.ThrowIfNull(operation);

            SoapOperationDescriptor descriptor = operation.Descriptor;
            if (descriptor is null)
            {
                throw new ArgumentException("Operation descriptor cannot be null.", nameof(operation));
            }

            if (!_operationsByAction.TryAdd(descriptor.Action, operation))
            {
                throw new InvalidOperationException($"Duplicate SOAP action '{descriptor.Action}' registered on endpoint.");
            }

            return this;
        }

        /// <summary>
        /// Validates <paramref name="options"/> and builds an immutable <see cref="SoapOperationTable"/>.
        /// </summary>
        /// <param name="options">The endpoint options controlling protocol versions and actionless fallback.</param>
        /// <returns>An immutable <see cref="SoapOperationTable"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="options"/> contains invalid limits or flags.</exception>
        /// <exception cref="InvalidOperationException">Thrown when actionless QName fallback is active and multiple operations share the same request element qualified name.</exception>
        public SoapOperationTable Build(SoapEndpointOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            options.Validate();

            FrozenDictionary<string, ISoapOperation> byAction = _operationsByAction.ToFrozenDictionary(StringComparer.Ordinal);
            bool enableQNameFallback = options.AllowMissingSoap11Action || (options.EnabledVersions & SoapVersionFlags.Soap12) != 0;

            if (!enableQNameFallback)
            {
                return new SoapOperationTable(byAction, null, []);
            }

            Dictionary<SoapQName, ISoapOperation> byRequestElement = new Dictionary<SoapQName, ISoapOperation>(_operationsByAction.Count);
            ISoapOperation[] fallbackOperations = new ISoapOperation[_operationsByAction.Count];
            int index = 0;

            foreach (ISoapOperation operation in _operationsByAction.Values)
            {
                SoapQName requestElement = operation.Descriptor.RequestElement;
                if (!byRequestElement.TryAdd(requestElement, operation))
                {
                    throw new InvalidOperationException(
                        $"Ambiguous SOAP request element '{requestElement}' cannot be used with missing-action or SOAP 1.2 routing.");
                }

                fallbackOperations[index++] = operation;
            }

            return new SoapOperationTable(
                byAction,
                byRequestElement.ToFrozenDictionary(),
                fallbackOperations);
        }

        /// <summary>
        /// Registers a strongly typed SOAP operation backed by source-generated <see cref="XmlTypeInfo{T}"/> metadata.
        /// </summary>
        /// <typeparam name="TRequest">The request wire DTO type.</typeparam>
        /// <typeparam name="TResponse">The response wire DTO type.</typeparam>
        /// <param name="descriptor">The validated operation descriptor.</param>
        /// <param name="requestTypeInfo">The source-generated <see cref="XmlTypeInfo{T}"/> for <typeparamref name="TRequest"/>.</param>
        /// <param name="responseTypeInfo">The source-generated <see cref="XmlTypeInfo{T}"/> for <typeparamref name="TResponse"/>.</param>
        /// <param name="handler">The typed asynchronous operation handler delegate.</param>
        /// <returns>This builder instance for fluent chaining.</returns>
        public SoapOperationTableBuilder MapOperation<TRequest, TResponse>(
            SoapOperationDescriptor descriptor,
            XmlTypeInfo<TRequest> requestTypeInfo,
            XmlTypeInfo<TResponse> responseTypeInfo,
            SoapOperationHandler<TRequest, TResponse> handler)
        {
            SoapOperation<TRequest, TResponse> operation = new SoapOperation<TRequest, TResponse>(
                descriptor,
                requestTypeInfo,
                responseTypeInfo,
                handler);

            return AddOperation(operation);
        }

        #endregion

    }

}
