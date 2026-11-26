using System;

namespace FastSoap.Abstractions
{

    /// <summary>
    /// Represents an owned, immutable SOAP header block extracted during synchronous envelope parsing.
    /// </summary>
    /// <remarks>
    /// <see cref="RawUtf8Payload"/> owns its backing memory so handlers can process headers asynchronously
    /// after pooled transport request buffers have been returned.
    /// </remarks>
    public sealed class SoapHeaderValue
    {

        #region Properties

        /// <summary>
        /// Gets the SOAP 1.1 <c>actor</c> or SOAP 1.2 <c>role</c> URI targeted by this header, or <see langword="null"/> if unspecified.
        /// </summary>
        public string? ActorOrRole { get; }

        /// <summary>
        /// Gets a value indicating whether the header carried a true <c>mustUnderstand</c> attribute.
        /// </summary>
        public bool MustUnderstand { get; }

        /// <summary>
        /// Gets the expanded qualified name of the SOAP header element.
        /// </summary>
        public SoapQName QName { get; }

        /// <summary>
        /// Gets the owned raw UTF-8 XML bytes of the header element.
        /// </summary>
        public ReadOnlyMemory<byte> RawUtf8Payload { get; }

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new <see cref="SoapHeaderValue"/> over an owned UTF-8 memory slice.
        /// </summary>
        /// <param name="qName">The validated qualified name of the header element.</param>
        /// <param name="actorOrRole">Optional <c>actor</c> or <c>role</c> URI.</param>
        /// <param name="mustUnderstand">Whether <c>mustUnderstand</c> is enabled on this header.</param>
        /// <param name="rawUtf8Payload">Owned immutable UTF-8 bytes for the header block.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="qName"/> is uninitialized or <paramref name="actorOrRole"/> is empty or whitespace.</exception>
        public SoapHeaderValue(
            SoapQName qName,
            string? actorOrRole,
            bool mustUnderstand,
            ReadOnlyMemory<byte> rawUtf8Payload)
        {
            if (string.IsNullOrWhiteSpace(qName.LocalName))
            {
                throw new ArgumentException("Header qualified name must be initialized.", nameof(qName));
            }

            if (actorOrRole is not null)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(actorOrRole);
            }

            QName = qName;
            ActorOrRole = actorOrRole;
            MustUnderstand = mustUnderstand;
            RawUtf8Payload = rawUtf8Payload;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Creates a new <see cref="SoapHeaderValue"/> by copying a borrowed UTF-8 payload span into owned memory.
        /// </summary>
        /// <param name="qName">The validated qualified name of the header element.</param>
        /// <param name="actorOrRole">Optional <c>actor</c> or <c>role</c> URI.</param>
        /// <param name="mustUnderstand">Whether <c>mustUnderstand</c> is enabled on this header.</param>
        /// <param name="utf8Payload">The borrowed UTF-8 bytes to copy.</param>
        /// <returns>An owned, immutable <see cref="SoapHeaderValue"/> instance.</returns>
        public static SoapHeaderValue CreateCopy(
            SoapQName qName,
            string? actorOrRole,
            bool mustUnderstand,
            ReadOnlySpan<byte> utf8Payload)
        {
            byte[] ownedPayload = utf8Payload.IsEmpty ? [] : utf8Payload.ToArray();

            return new SoapHeaderValue(qName, actorOrRole, mustUnderstand, ownedPayload);
        }

        #endregion

    }

}
