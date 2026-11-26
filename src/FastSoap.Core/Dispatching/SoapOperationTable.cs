using System;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

using FastSoap.Abstractions;
using FastSoap.Core.Serialization;

namespace FastSoap.Core.Dispatching
{

    /// <summary>
    /// Immutable, build-once operation routing table keyed by ordinal SOAP action URI with an optional
    /// secondary Body element qualified-name index for actionless routing.
    /// </summary>
    /// <remarks>
    /// Constructed exclusively via <see cref="SoapOperationTableBuilder"/> after endpoint options validation.
    /// </remarks>
    public sealed class SoapOperationTable
    {

        #region Fields

        private readonly ISoapOperation[] _fallbackOperations;
        private readonly FrozenDictionary<string, ISoapOperation>.AlternateLookup<ReadOnlySpan<char>> _operationsByActionLookup;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the immutable ordinal-keyed operation dictionary indexed by <see cref="SoapOperationDescriptor.Action"/>.
        /// </summary>
        public FrozenDictionary<string, ISoapOperation> OperationsByAction { get; }

        /// <summary>
        /// Gets the optional immutable Body qualified-name index when missing-action or SOAP 1.2 routing is enabled;
        /// otherwise <see langword="null"/>.
        /// </summary>
        public FrozenDictionary<SoapQName, ISoapOperation>? OperationsByRequestElement { get; }

        /// <summary>
        /// Gets a value indicating whether unambiguous actionless Body qualified-name routing is active.
        /// </summary>
        public bool SupportsActionlessRouting => OperationsByRequestElement is not null;

        #endregion

        #region Constructors

        internal SoapOperationTable(
            FrozenDictionary<string, ISoapOperation> operationsByAction,
            FrozenDictionary<SoapQName, ISoapOperation>? operationsByRequestElement,
            ISoapOperation[] fallbackOperations)
        {
            OperationsByAction = operationsByAction;
            _operationsByActionLookup = operationsByAction.GetAlternateLookup<ReadOnlySpan<char>>();
            OperationsByRequestElement = operationsByRequestElement;
            _fallbackOperations = fallbackOperations;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Attempts to resolve an operation by exact ordinal SOAP action URI without allocating a substring.
        /// </summary>
        /// <param name="action">The incoming SOAP action URI character span.</param>
        /// <param name="operation">When this method returns <see langword="true"/>, contains the matching operation.</param>
        /// <returns><see langword="true"/> if an operation was found; otherwise <see langword="false"/>.</returns>
        public bool TryGetByAction(ReadOnlySpan<char> action, [NotNullWhen(true)] out ISoapOperation? operation)
        {
            if (action.IsEmpty || action.IsWhiteSpace())
            {
                operation = null;

                return false;
            }

            return _operationsByActionLookup.TryGetValue(action, out operation);
        }

        /// <summary>
        /// Attempts to resolve an operation by exact ordinal SOAP action URI.
        /// </summary>
        /// <param name="action">The incoming SOAP action URI.</param>
        /// <param name="operation">When this method returns <see langword="true"/>, contains the matching operation.</param>
        /// <returns><see langword="true"/> if an operation was found; otherwise <see langword="false"/>.</returns>
        public bool TryGetByAction(string action, [NotNullWhen(true)] out ISoapOperation? operation)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                operation = null;

                return false;
            }

            return OperationsByAction.TryGetValue(action, out operation);
        }

        /// <summary>
        /// Attempts to resolve an operation by exact Body child element qualified name when actionless routing is enabled.
        /// </summary>
        /// <param name="requestElement">The incoming Body child element qualified name.</param>
        /// <param name="operation">When this method returns <see langword="true"/>, contains the matching operation.</param>
        /// <returns><see langword="true"/> if actionless routing is enabled and a matching operation exists; otherwise <see langword="false"/>.</returns>
        public bool TryGetByRequestElement(SoapQName requestElement, [NotNullWhen(true)] out ISoapOperation? operation)
        {
            if (OperationsByRequestElement is null)
            {
                operation = null;

                return false;
            }

            return OperationsByRequestElement.TryGetValue(requestElement, out operation);
        }

        #endregion

        #region Internal Methods

        internal bool TryGetByRequestElementUtf8(
            ReadOnlySpan<byte> namespaceUriUtf8,
            ReadOnlySpan<byte> localNameUtf8,
            [NotNullWhen(true)] out ISoapOperation? operation)
        {
            ISoapOperation[] fallbackOperations = _fallbackOperations;
            for (int index = 0; index < fallbackOperations.Length; index++)
            {
                ISoapOperation candidate = fallbackOperations[index];
                if (candidate.Descriptor.RequestElement.Matches(namespaceUriUtf8, localNameUtf8))
                {
                    operation = candidate;

                    return true;
                }
            }

            operation = null;

            return false;
        }

        #endregion

    }

}
