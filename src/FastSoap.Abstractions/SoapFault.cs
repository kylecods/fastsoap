using System;

namespace FastSoap.Abstractions
{

    /// <summary>
    /// Represents a protocol-normalized SOAP 1.1 or SOAP 1.2 fault model for safe wire serialization.
    /// </summary>
    /// <remarks>
    /// Fault reasons must contain safe public messages only and never expose raw exception stacks or secrets.
    /// </remarks>
    public sealed class SoapFault
    {

        #region Fields

        private const string Soap11EnvelopeNamespace = "http://schemas.xmlsoap.org/soap/envelope/";
        private const string Soap12EnvelopeNamespace = "http://www.w3.org/2003/05/soap-envelope";

        #endregion

        #region Properties

        /// <summary>
        /// Gets the optional SOAP 1.1 <c>faultactor</c> or SOAP 1.2 <c>Node</c>/<c>Role</c> URI.
        /// </summary>
        public string? ActorOrNode { get; }

        /// <summary>
        /// Gets the XML local name of the fault code.
        /// </summary>
        public string CodeLocalName { get; }

        /// <summary>
        /// Gets the XML namespace URI of the fault code.
        /// </summary>
        public string CodeNamespace { get; }

        /// <summary>
        /// Gets the optional correlation identifier associated with this fault.
        /// </summary>
        public string? CorrelationId { get; }

        /// <summary>
        /// Gets optional pre-serialized UTF-8 XML bytes for the fault <c>detail</c>/<c>Detail</c> element.
        /// </summary>
        public ReadOnlyMemory<byte> DetailXmlUtf8 { get; }

        /// <summary>
        /// Gets the normalized SOAP fault classification.
        /// </summary>
        public SoapFaultKind Kind { get; }

        /// <summary>
        /// Gets the safe, public human-readable fault reason.
        /// </summary>
        public string Reason { get; }

        /// <summary>
        /// Gets the SOAP version in which this fault should be serialized.
        /// </summary>
        public SoapVersion Version { get; }

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new validated <see cref="SoapFault"/> instance.
        /// </summary>
        /// <param name="version">Target SOAP protocol version.</param>
        /// <param name="kind">Normalized SOAP fault classification.</param>
        /// <param name="codeNamespace">XML namespace URI of the fault code.</param>
        /// <param name="codeLocalName">XML local name of the fault code.</param>
        /// <param name="reason">Safe public fault reason string.</param>
        /// <param name="actorOrNode">Optional fault actor or node URI.</param>
        /// <param name="detailXmlUtf8">Optional UTF-8 XML detail payload.</param>
        /// <param name="correlationId">Optional correlation identifier.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="version"/> or <paramref name="kind"/> is undefined.</exception>
        /// <exception cref="ArgumentException">Thrown when required strings are blank or optional identifiers are whitespace.</exception>
        public SoapFault(
            SoapVersion version,
            SoapFaultKind kind,
            string codeNamespace,
            string codeLocalName,
            string reason,
            string? actorOrNode = null,
            ReadOnlyMemory<byte> detailXmlUtf8 = default,
            string? correlationId = null)
        {
            if (!Enum.IsDefined(version))
            {
                throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported SOAP version.");
            }

            if (!Enum.IsDefined(kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported SOAP fault kind.");
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(codeNamespace);
            ArgumentException.ThrowIfNullOrWhiteSpace(codeLocalName);
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);

            if (actorOrNode is not null)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(actorOrNode);
            }

            if (correlationId is not null)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
            }

            Version = version;
            Kind = kind;
            CodeNamespace = codeNamespace;
            CodeLocalName = codeLocalName;
            Reason = reason;
            ActorOrNode = actorOrNode;
            DetailXmlUtf8 = detailXmlUtf8;
            CorrelationId = correlationId;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Creates a <see cref="SoapFault"/> populated with the canonical SOAP 1.1 or SOAP 1.2 fault code for <paramref name="kind"/>.
        /// </summary>
        /// <param name="version">Target SOAP protocol version.</param>
        /// <param name="kind">Normalized SOAP fault classification.</param>
        /// <param name="reason">Safe public fault reason string.</param>
        /// <param name="actorOrNode">Optional fault actor or node URI.</param>
        /// <param name="detailXmlUtf8">Optional UTF-8 XML detail payload.</param>
        /// <param name="correlationId">Optional correlation identifier.</param>
        /// <returns>A validated <see cref="SoapFault"/> using the standard envelope fault code QName.</returns>
        public static SoapFault CreateStandard(
            SoapVersion version,
            SoapFaultKind kind,
            string reason,
            string? actorOrNode = null,
            ReadOnlyMemory<byte> detailXmlUtf8 = default,
            string? correlationId = null)
        {
            if (!Enum.IsDefined(version))
            {
                throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported SOAP version.");
            }

            if (!Enum.IsDefined(kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported SOAP fault kind.");
            }

            string codeNamespace = version == SoapVersion.Soap11
                ? Soap11EnvelopeNamespace
                : Soap12EnvelopeNamespace;

            string codeLocalName = (version, kind) switch
            {
                (SoapVersion.Soap11, SoapFaultKind.Sender) => "Client",
                (SoapVersion.Soap11, SoapFaultKind.Receiver) => "Server",
                (SoapVersion.Soap12, SoapFaultKind.Sender) => "Sender",
                (SoapVersion.Soap12, SoapFaultKind.Receiver) => "Receiver",
                (_, SoapFaultKind.VersionMismatch) => "VersionMismatch",
                (_, SoapFaultKind.MustUnderstand) => "MustUnderstand",
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported SOAP fault kind.")
            };

            return new SoapFault(
                version,
                kind,
                codeNamespace,
                codeLocalName,
                reason,
                actorOrNode,
                detailXmlUtf8,
                correlationId);
        }

        #endregion

    }

}
