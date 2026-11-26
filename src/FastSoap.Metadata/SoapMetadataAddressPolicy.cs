using System;
using FastSoap.Metadata.Internal;

namespace FastSoap.Metadata
{

    /// <summary>
    /// Enforces an explicit configured HTTP or HTTPS public endpoint URI policy for WSDL and XSD metadata
    /// without ever trusting incoming <c>Host</c> or <c>X-Forwarded-Host</c> headers.
    /// </summary>
    /// <example>
    /// <code>
    /// SoapMetadataAddressPolicy policy = new SoapMetadataAddressPolicy("https://api.example.com/PaymentService.svc");
    /// string endpointLocation = policy.GetEndpointAddress();
    /// string schemaLocation = policy.GetSchemaLocation("xsd0");
    /// </code>
    /// </example>
    /// <remarks>
    /// Rejects non-HTTP/HTTPS schemes, relative URIs, embedded user credentials, query strings, and fragments fail-first.
    /// </remarks>
    public sealed class SoapMetadataAddressPolicy
    {

        #region Properties

        /// <summary>
        /// Gets the canonicalized absolute HTTP or HTTPS endpoint address emitted in WSDL <c>address</c> elements.
        /// </summary>
        public string CanonicalEndpointAddress { get; }

        /// <summary>
        /// Gets the validated public endpoint <see cref="Uri"/>.
        /// </summary>
        public Uri PublicEndpointUri { get; }

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new <see cref="SoapMetadataAddressPolicy"/> from an explicit public endpoint address string.
        /// </summary>
        /// <param name="publicEndpointAddress">The absolute HTTP or HTTPS public endpoint URL.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="publicEndpointAddress"/> is null, whitespace, relative, non-HTTP/HTTPS, or contains userinfo, query, or fragment components.</exception>
        public SoapMetadataAddressPolicy(string publicEndpointAddress)
            : this(ParseAndValidate(publicEndpointAddress))
        {
        }

        /// <summary>
        /// Initializes a new <see cref="SoapMetadataAddressPolicy"/> from an explicit public endpoint <see cref="Uri"/>.
        /// </summary>
        /// <param name="publicEndpointUri">The absolute HTTP or HTTPS public endpoint <see cref="Uri"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="publicEndpointUri"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="publicEndpointUri"/> is relative, non-HTTP/HTTPS, or contains userinfo, query, or fragment components.</exception>
        public SoapMetadataAddressPolicy(Uri publicEndpointUri)
        {
            ArgumentNullException.ThrowIfNull(publicEndpointUri);
            ValidateUri(publicEndpointUri, nameof(publicEndpointUri));

            PublicEndpointUri = publicEndpointUri;
            CanonicalEndpointAddress = publicEndpointUri.GetComponents(
                UriComponents.SchemeAndServer | UriComponents.Path,
                UriFormat.UriEscaped);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Creates a validated <see cref="SoapMetadataAddressPolicy"/> from a public endpoint URL string.
        /// </summary>
        /// <param name="publicEndpointAddress">The absolute HTTP or HTTPS public endpoint URL.</param>
        /// <returns>The validated <see cref="SoapMetadataAddressPolicy"/>.</returns>
        public static SoapMetadataAddressPolicy Create(string publicEndpointAddress)
        {
            return new SoapMetadataAddressPolicy(publicEndpointAddress);
        }

        /// <summary>
        /// Returns the configured canonical endpoint address, strictly ignoring untrusted request headers.
        /// </summary>
        /// <returns>The configured public endpoint address.</returns>
        public string GetEndpointAddress()
        {
            return CanonicalEndpointAddress;
        }

        /// <summary>
        /// Returns the configured canonical endpoint address, explicitly ignoring any untrusted <c>Host</c> or <c>X-Forwarded-Host</c> header values.
        /// </summary>
        /// <param name="untrustedHostHeader">Untrusted <c>Host</c> header value (always ignored).</param>
        /// <param name="untrustedForwardedHostHeader">Untrusted <c>X-Forwarded-Host</c> header value (always ignored).</param>
        /// <returns>The configured public endpoint address.</returns>
        public string GetEndpointAddress(string? untrustedHostHeader, string? untrustedForwardedHostHeader)
        {
            _ = untrustedHostHeader;
            _ = untrustedForwardedHostHeader;

            return CanonicalEndpointAddress;
        }

        /// <summary>
        /// Builds a validated schema import location URI rooted at the configured public endpoint address.
        /// </summary>
        /// <param name="schemaName">The normalized schema name (for example, <c>xsd0</c> or <c>payments.xsd</c>).</param>
        /// <returns>The absolute HTTP or HTTPS schema query location (<c>{Endpoint}?xsd={schemaName}</c>).</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="schemaName"/> fails strict path normalization.</exception>
        public string GetSchemaLocation(string schemaName)
        {
            if (!SoapMetadataPathNormalizer.TryNormalizeSchemaName(schemaName, out string? normalizedName))
            {
                throw new ArgumentException("Schema name contains invalid, traversal, query, or URL characters.", nameof(schemaName));
            }

            return $"{CanonicalEndpointAddress}?xsd={normalizedName}";
        }

        #endregion

        #region Private Methods

        private static Uri ParseAndValidate(string publicEndpointAddress)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(publicEndpointAddress);

            if (!Uri.TryCreate(publicEndpointAddress, UriKind.Absolute, out Uri? uri))
            {
                throw new ArgumentException("Public endpoint address must be a valid absolute HTTP or HTTPS URI.", nameof(publicEndpointAddress));
            }

            ValidateUri(uri, nameof(publicEndpointAddress));

            return uri;
        }

        private static void ValidateUri(Uri uri, string paramName)
        {
            if (!uri.IsAbsoluteUri)
            {
                throw new ArgumentException("Public endpoint URI must be absolute.", paramName);
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Public endpoint URI must use the http or https scheme.", paramName);
            }

            if (string.IsNullOrWhiteSpace(uri.Host))
            {
                throw new ArgumentException("Public endpoint URI must specify a valid host.", paramName);
            }

            if (!string.IsNullOrWhiteSpace(uri.UserInfo))
            {
                throw new ArgumentException("Public endpoint URI cannot contain embedded user credentials.", paramName);
            }

            if (!string.IsNullOrWhiteSpace(uri.Query))
            {
                throw new ArgumentException("Public endpoint URI cannot contain a query string.", paramName);
            }

            if (!string.IsNullOrWhiteSpace(uri.Fragment))
            {
                throw new ArgumentException("Public endpoint URI cannot contain a fragment.", paramName);
            }
        }

        #endregion

    }

}
