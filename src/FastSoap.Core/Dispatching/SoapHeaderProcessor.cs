using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using FastSoap.Abstractions;
using FastSoap.Core.Protocol;

namespace FastSoap.Core.Dispatching
{

    /// <summary>
    /// Executes explicitly registered <see cref="ISoapHeaderHandler"/> instances and enforces
    /// SOAP 1.1 <c>actor</c> and SOAP 1.2 <c>role</c> plus <c>mustUnderstand</c> rules.
    /// </summary>
    /// <remarks>
    /// Unknown optional targeted headers and non-targeted headers are ignored; unprocessed targeted
    /// mandatory headers raise a <see cref="SoapFaultKind.MustUnderstand"/> protocol exception.
    /// </remarks>
    public sealed class SoapHeaderProcessor
    {

        #region Fields

        private readonly FrozenDictionary<SoapQName, ISoapHeaderHandler> _handlers;

        #endregion

        #region Properties

        /// <summary>
        /// Gets an empty <see cref="SoapHeaderProcessor"/> with no registered header handlers.
        /// </summary>
        public static SoapHeaderProcessor Empty { get; } = new SoapHeaderProcessor();

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new <see cref="SoapHeaderProcessor"/> with the supplied explicit header handlers.
        /// </summary>
        /// <param name="handlers">Optional registered <see cref="ISoapHeaderHandler"/> instances keyed by <see cref="ISoapHeaderHandler.HeaderName"/>.</param>
        /// <exception cref="ArgumentException">Thrown when a handler element is <see langword="null"/> or has an uninitialized <see cref="ISoapHeaderHandler.HeaderName"/>.</exception>
        /// <exception cref="InvalidOperationException">Thrown when duplicate handlers are registered for the same header qualified name.</exception>
        public SoapHeaderProcessor(IEnumerable<ISoapHeaderHandler>? handlers = null)
        {
            if (handlers is null)
            {
                _handlers = FrozenDictionary<SoapQName, ISoapHeaderHandler>.Empty;

                return;
            }

            Dictionary<SoapQName, ISoapHeaderHandler> map = new Dictionary<SoapQName, ISoapHeaderHandler>();
            foreach (ISoapHeaderHandler handler in handlers)
            {
                if (handler is null)
                {
                    throw new ArgumentException("SOAP header handler collection cannot contain null entries.", nameof(handlers));
                }

                if (string.IsNullOrWhiteSpace(handler.HeaderName.LocalName))
                {
                    throw new ArgumentException("SOAP header handler must declare an initialized HeaderName.", nameof(handlers));
                }

                if (!map.TryAdd(handler.HeaderName, handler))
                {
                    throw new InvalidOperationException($"Duplicate SOAP header handler registered for '{handler.HeaderName}'.");
                }
            }

            _handlers = map.ToFrozenDictionary();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Collects the qualified names of all mandatory headers targeted at this ultimate receiver that do not have
        /// a registered <see cref="ISoapHeaderHandler"/>.
        /// </summary>
        /// <param name="version">The SOAP protocol version of the message.</param>
        /// <param name="headers">The parsed header blocks.</param>
        /// <returns>The list of unprocessed mandatory header qualified names.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="version"/> is undefined.</exception>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="headers"/> is <see langword="null"/>.</exception>
        public IReadOnlyList<SoapQName> GetUnprocessedMandatoryHeaders(
            SoapVersion version,
            IReadOnlyList<SoapHeaderValue> headers)
        {
            if (!Enum.IsDefined(version))
            {
                throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported SOAP version.");
            }

            ArgumentNullException.ThrowIfNull(headers);

            List<SoapQName>? notUnderstood = null;
            for (int index = 0; index < headers.Count; index++)
            {
                SoapHeaderValue header = headers[index];
                if (!header.MustUnderstand || !IsTargetedAtUltimateReceiver(version, header.ActorOrRole))
                {
                    continue;
                }

                if (!_handlers.ContainsKey(header.QName))
                {
                    notUnderstood ??= new List<SoapQName>(2);
                    notUnderstood.Add(header.QName);
                }
            }

            return notUnderstood is null ? [] : notUnderstood;
        }

        /// <summary>
        /// Determines whether a header's <c>actor</c> (SOAP 1.1) or <c>role</c> (SOAP 1.2) URI targets this ultimate receiver node.
        /// </summary>
        /// <param name="version">The SOAP protocol version.</param>
        /// <param name="actorOrRole">The header's <c>actor</c> or <c>role</c> URI, or <see langword="null"/> when omitted.</param>
        /// <returns><see langword="true"/> if this node must process or understand the header; otherwise <see langword="false"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="version"/> is undefined.</exception>
        public static bool IsTargetedAtUltimateReceiver(SoapVersion version, string? actorOrRole)
        {
            if (!Enum.IsDefined(version))
            {
                throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported SOAP version.");
            }

            if (actorOrRole is null)
            {
                return true;
            }

            if (version == SoapVersion.Soap11)
            {
                return string.Equals(actorOrRole, SoapConstants.Soap11NextActorUri, StringComparison.Ordinal);
            }

            if (string.Equals(actorOrRole, SoapConstants.Soap12NoneRoleUri, StringComparison.Ordinal))
            {
                return false;
            }

            return string.Equals(actorOrRole, SoapConstants.Soap12UltimateReceiverRoleUri, StringComparison.Ordinal) ||
                string.Equals(actorOrRole, SoapConstants.Soap12NextRoleUri, StringComparison.Ordinal);
        }

        /// <summary>
        /// Processes all targeted SOAP headers in <paramref name="context"/> and throws a
        /// <see cref="SoapFaultKind.MustUnderstand"/> fault if any targeted mandatory header is unhandled.
        /// </summary>
        /// <param name="context">The current SOAP call context.</param>
        /// <param name="cancellationToken">A cancellation token tied to the request.</param>
        /// <returns>A <see cref="ValueTask"/> representing the asynchronous header processing operation.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is <see langword="null"/>.</exception>
        /// <exception cref="SoapProtocolException">Thrown when a targeted mandatory header has no registered handler.</exception>
        public async ValueTask ProcessAsync(SoapCallContext context, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);

            IReadOnlyList<SoapHeaderValue> headers = context.Headers;
            if (headers.Count == 0)
            {
                return;
            }

            for (int index = 0; index < headers.Count; index++)
            {
                SoapHeaderValue header = headers[index];
                if (header.MustUnderstand &&
                    IsTargetedAtUltimateReceiver(context.Version, header.ActorOrRole) &&
                    !_handlers.ContainsKey(header.QName))
                {
                    throw new SoapProtocolException(
                        SoapFaultKind.MustUnderstand,
                        HttpStatusCode.InternalServerError,
                        "One or more mandatory SOAP headers were not understood.",
                        context.Version);
                }
            }

            for (int index = 0; index < headers.Count; index++)
            {
                SoapHeaderValue header = headers[index];
                if (IsTargetedAtUltimateReceiver(context.Version, header.ActorOrRole) &&
                    _handlers.TryGetValue(header.QName, out ISoapHeaderHandler? handler))
                {
                    await handler.HandleAsync(header, context, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        #endregion

    }

}
