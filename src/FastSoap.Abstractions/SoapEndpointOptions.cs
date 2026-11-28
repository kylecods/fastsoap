using System;

namespace FastSoap.Abstractions
{

    /// <summary>
    /// Configures protocol, security quota, and buffer behavior for a FastSoap endpoint.
    /// </summary>
    /// <remarks>
    /// Call <see cref="Validate"/> at startup before freezing endpoint tables to fail fast on invalid limits.
    /// </remarks>
    public sealed class SoapEndpointOptions
    {

        #region Properties

        /// <summary>
        /// Gets or sets a value indicating whether incoming HTTP requests compressed with
        /// <c>Content-Encoding: gzip</c>, <c>br</c>, or <c>deflate</c> are accepted and decompressed
        /// within <see cref="MaxRequestBytes"/>. Defaults to <see langword="false"/>.
        /// </summary>
        public bool AllowCompressedRequests { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether SOAP 1.1 requests with a missing or empty <c>SOAPAction</c>
        /// header may fall back to unambiguous Body element qualified name routing. Defaults to <see langword="false"/>.
        /// </summary>
        public bool AllowMissingSoap11Action { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether pooled request/response byte buffers are zero-cleared before return.
        /// Defaults to <see langword="true"/>.
        /// </summary>
        public bool ClearPooledBuffers { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether successful SOAP responses are written directly to the HTTP
        /// response <c>PipeWriter</c> without a staging buffer when response compression is not negotiated.
        /// Defaults to <see langword="false"/>.
        /// </summary>
        /// <remarks>
        /// When enabled, response serialization failures occurring after HTTP headers start abort the connection
        /// instead of appending a fault envelope. If <see cref="EnableResponseCompression"/> is also enabled and the
        /// client negotiates <c>br</c>, <c>gzip</c>, or <c>deflate</c>, FastSoap falls back to staged buffering so the
        /// threshold check and <c>Content-Length</c> header remain accurate.
        /// </remarks>
        public bool EnableDirectResponseStreaming { get; set; }

        /// <summary>
        /// Gets or sets the SOAP protocol versions enabled for this endpoint.
        /// Defaults to <see cref="SoapVersionFlags.Soap11"/>.
        /// </summary>
        public SoapVersionFlags EnabledVersions { get; set; } = SoapVersionFlags.Soap11;

        /// <summary>
        /// Gets or sets a value indicating whether static WSDL/XSD metadata GET endpoints are enabled.
        /// Defaults to <see langword="false"/>.
        /// </summary>
        public bool EnableMetadata { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether staged responses meeting <see cref="ResponseCompressionMinBytes"/>
        /// are compressed using <c>br</c>, <c>gzip</c>, or <c>deflate</c> when negotiated via <c>Accept-Encoding</c>.
        /// Defaults to <see langword="false"/>.
        /// </summary>
        public bool EnableResponseCompression { get; set; }

        /// <summary>
        /// Gets or sets the maximum allowed length in characters for an incoming SOAP action header or parameter.
        /// Defaults to <c>1024</c>.
        /// </summary>
        public int MaxActionLength { get; set; } = 1024;

        /// <summary>
        /// Gets or sets the maximum number of attributes permitted on a single XML element.
        /// Defaults to <c>64</c>.
        /// </summary>
        public int MaxAttributesPerElement { get; set; } = 64;

        /// <summary>
        /// Gets or sets the maximum cumulative byte budget for SOAP header blocks in a single request.
        /// Defaults to <c>65536</c> (64 KiB).
        /// </summary>
        public int MaxHeaderBytes { get; set; } = 64 * 1024;

        /// <summary>
        /// Gets or sets the maximum number of direct child blocks allowed inside <c>Header</c>.
        /// Defaults to <c>32</c>.
        /// </summary>
        public int MaxHeaderCount { get; set; } = 32;

        /// <summary>
        /// Gets or sets the maximum bounded request payload size in bytes (including decompressed size when
        /// <see cref="AllowCompressedRequests"/> is enabled). Defaults to <c>1048576</c> (1 MiB).
        /// </summary>
        public int MaxRequestBytes { get; set; } = 1024 * 1024;

        /// <summary>
        /// Gets or sets the maximum staged response payload size in bytes.
        /// Defaults to <c>2097152</c> (2 MiB).
        /// </summary>
        public int MaxResponseBytes { get; set; } = 2 * 1024 * 1024;

        /// <summary>
        /// Gets or sets the maximum XML element nesting depth allowed while reading requests.
        /// Defaults to <c>64</c>.
        /// </summary>
        public int MaxXmlDepth { get; set; } = 64;

        /// <summary>
        /// Gets or sets the minimum staged response payload length in bytes before response compression is applied.
        /// Defaults to <c>1024</c>.
        /// </summary>
        public int ResponseCompressionMinBytes { get; set; } = 1024;

        #endregion

        #region Public Methods

        /// <summary>
        /// Validates all configured quotas and protocol flags, throwing immediately on invalid configuration.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when any quota or <see cref="EnabledVersions"/> flag combination is out of range.</exception>
        public void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(MaxRequestBytes, 0);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(MaxResponseBytes, 0);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(MaxXmlDepth, 0);
            ArgumentOutOfRangeException.ThrowIfNegative(MaxHeaderBytes);
            ArgumentOutOfRangeException.ThrowIfNegative(MaxHeaderCount);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(MaxAttributesPerElement, 0);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(MaxActionLength, 0);
            ArgumentOutOfRangeException.ThrowIfNegative(ResponseCompressionMinBytes);

            if (EnabledVersions == SoapVersionFlags.None || (EnabledVersions & ~SoapVersionFlags.Both) != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(EnabledVersions),
                    EnabledVersions,
                    "At least one valid SOAP protocol version flag (Soap11 and/or Soap12) must be enabled.");
            }
        }

        #endregion

    }

}
