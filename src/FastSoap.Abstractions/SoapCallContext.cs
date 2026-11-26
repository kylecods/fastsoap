using System;
using System.Collections.Generic;
using System.Security.Claims;

namespace FastSoap.Abstractions
{

    /// <summary>
    /// Provides per-request execution context passed to typed SOAP operation handlers and header handlers.
    /// </summary>
    /// <remarks>
    /// This context is scoped to a single request and must not be retained after the operation completes.
    /// </remarks>
    public sealed class SoapCallContext
    {

        #region Properties

        /// <summary>
        /// Gets the resolved SOAP action URI for the current request.
        /// </summary>
        public required string Action { get; init; }

        /// <summary>
        /// Gets the owned, immutable SOAP headers parsed from the request envelope.
        /// </summary>
        public IReadOnlyList<SoapHeaderValue> Headers { get; init; } = [];

        /// <summary>
        /// Gets the matched immutable descriptor for the target SOAP operation.
        /// </summary>
        public required SoapOperationDescriptor OperationDescriptor { get; init; }

        /// <summary>
        /// Gets the request-scoped <see cref="IServiceProvider"/>.
        /// </summary>
        public required IServiceProvider Services { get; init; }

        /// <summary>
        /// Gets the authenticated <see cref="ClaimsPrincipal"/> associated with the HTTP request.
        /// </summary>
        public required ClaimsPrincipal User { get; init; }

        /// <summary>
        /// Gets the SOAP protocol version of the incoming request.
        /// </summary>
        public required SoapVersion Version { get; init; }

        #endregion

    }

}
