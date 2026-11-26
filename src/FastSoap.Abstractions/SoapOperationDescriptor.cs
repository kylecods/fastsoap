using System;
using System.Buffers;
using System.Text;

namespace FastSoap.Abstractions
{

    /// <summary>
    /// Describes an immutable, validated SOAP operation contract, including its action URIs,
    /// expected request/response Body element qualified names, and pre-encoded UTF-8 byte tokens.
    /// </summary>
    /// <example>
    /// <code>
    /// SoapOperationDescriptor descriptor = new SoapOperationDescriptor(
    ///     name: "Transfer",
    ///     action: "urn:payments/IPaymentService/Transfer",
    ///     replyAction: "urn:payments/IPaymentService/TransferResponse",
    ///     requestElement: new SoapQName("urn:payments", "Transfer"),
    ///     responseElement: new SoapQName("urn:payments", "TransferResponse"));
    /// </code>
    /// </example>
    public sealed record SoapOperationDescriptor
    {

        #region Fields

        private static readonly SearchValues<char> ForbiddenActionChars = SearchValues.Create("\0\r\n");

        private readonly byte[] _actionUtf8;
        private readonly byte[]? _replyActionUtf8;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the canonical SOAP action URI for this operation.
        /// </summary>
        public string Action { get; }

        /// <summary>
        /// Gets the pre-encoded UTF-8 bytes of <see cref="Action"/>.
        /// </summary>
        public ReadOnlySpan<byte> ActionUtf8 => _actionUtf8;

        /// <summary>
        /// Gets the logical operation name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the optional reply action URI for response metadata and diagnostics.
        /// </summary>
        public string? ReplyAction { get; }

        /// <summary>
        /// Gets the pre-encoded UTF-8 bytes of <see cref="ReplyAction"/>, or an empty span if unset.
        /// </summary>
        public ReadOnlySpan<byte> ReplyActionUtf8 => _replyActionUtf8;

        /// <summary>
        /// Gets the expected expanded qualified name of the direct <c>Body</c> child element in requests.
        /// </summary>
        public SoapQName RequestElement { get; }

        /// <summary>
        /// Gets the expanded qualified name of the direct <c>Body</c> child element emitted in responses.
        /// </summary>
        public SoapQName ResponseElement { get; }

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new validated <see cref="SoapOperationDescriptor"/> and pre-encodes its UTF-8 tokens.
        /// </summary>
        /// <param name="name">The non-empty logical operation name.</param>
        /// <param name="action">The non-empty SOAP action URI.</param>
        /// <param name="replyAction">Optional reply action URI.</param>
        /// <param name="requestElement">The validated request Body element qualified name.</param>
        /// <param name="responseElement">The validated response Body element qualified name.</param>
        /// <exception cref="ArgumentException">Thrown when any required string is blank, contains control characters, or a <see cref="SoapQName"/> is uninitialized.</exception>
        public SoapOperationDescriptor(
            string name,
            string action,
            string? replyAction,
            SoapQName requestElement,
            SoapQName responseElement)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentException.ThrowIfNullOrWhiteSpace(action);

            if (action.AsSpan().ContainsAny(ForbiddenActionChars))
            {
                throw new ArgumentException("SOAP action cannot contain null or newline characters.", nameof(action));
            }

            if (replyAction is not null)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(replyAction);
                if (replyAction.AsSpan().ContainsAny(ForbiddenActionChars))
                {
                    throw new ArgumentException("SOAP reply action cannot contain null or newline characters.", nameof(replyAction));
                }
            }

            if (string.IsNullOrWhiteSpace(requestElement.LocalName))
            {
                throw new ArgumentException("Request element qualified name must be initialized.", nameof(requestElement));
            }

            if (string.IsNullOrWhiteSpace(responseElement.LocalName))
            {
                throw new ArgumentException("Response element qualified name must be initialized.", nameof(responseElement));
            }

            Name = name;
            Action = action;
            ReplyAction = replyAction;
            RequestElement = requestElement;
            ResponseElement = responseElement;
            _actionUtf8 = Encoding.UTF8.GetBytes(action);
            _replyActionUtf8 = replyAction is null ? null : Encoding.UTF8.GetBytes(replyAction);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Determines whether this descriptor is equal to another using ordinal string and QName equality.
        /// </summary>
        /// <param name="other">The other descriptor to compare.</param>
        /// <returns><see langword="true"/> if all descriptor components match ordinally; otherwise <see langword="false"/>.</returns>
        public bool Equals(SoapOperationDescriptor? other)
        {
            if (other is null)
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return string.Equals(Name, other.Name, StringComparison.Ordinal)
                && string.Equals(Action, other.Action, StringComparison.Ordinal)
                && string.Equals(ReplyAction, other.ReplyAction, StringComparison.Ordinal)
                && RequestElement.Equals(other.RequestElement)
                && ResponseElement.Equals(other.ResponseElement);
        }

        /// <summary>
        /// Computes an ordinal hash code for the descriptor.
        /// </summary>
        /// <returns>The combined ordinal hash code.</returns>
        public override int GetHashCode()
        {
            return HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(Name),
                StringComparer.Ordinal.GetHashCode(Action),
                ReplyAction is null ? 0 : StringComparer.Ordinal.GetHashCode(ReplyAction),
                RequestElement,
                ResponseElement);
        }

        #endregion

    }

}
