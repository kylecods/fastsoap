using System;

namespace FastSoap.Core.Protocol
{

    /// <summary>
    /// Provides RVA-backed UTF-8 byte spans and canonical string constants for SOAP 1.1 and SOAP 1.2 protocols.
    /// </summary>
    /// <remarks>
    /// Byte span properties return RVA-backed metadata slices with zero heap allocation per access.
    /// </remarks>
    public static class SoapConstants
    {

        #region Fields

        /// <summary>
        /// Default byte limit for staged SOAP fault responses (16 KiB).
        /// </summary>
        public const int DefaultFaultBytesLimit = 16 * 1024;

        /// <summary>
        /// Canonical SOAP 1.1 media type (<c>text/xml</c>).
        /// </summary>
        public const string Soap11MediaType = "text/xml";

        /// <summary>
        /// Canonical SOAP 1.1 envelope XML namespace URI.
        /// </summary>
        public const string Soap11Namespace = "http://schemas.xmlsoap.org/soap/envelope/";

        /// <summary>
        /// Canonical SOAP 1.1 <c>next</c> actor URI.
        /// </summary>
        public const string Soap11NextActorUri = "http://schemas.xmlsoap.org/soap/actor/next";

        /// <summary>
        /// Canonical SOAP 1.1 HTTP response <c>Content-Type</c> header value.
        /// </summary>
        public const string Soap11ResponseContentType = "text/xml; charset=utf-8";

        /// <summary>
        /// Canonical SOAP 1.2 media type (<c>application/soap+xml</c>).
        /// </summary>
        public const string Soap12MediaType = "application/soap+xml";

        /// <summary>
        /// Canonical SOAP 1.2 envelope XML namespace URI.
        /// </summary>
        public const string Soap12Namespace = "http://www.w3.org/2003/05/soap-envelope";

        /// <summary>
        /// Canonical SOAP 1.2 <c>next</c> role URI.
        /// </summary>
        public const string Soap12NextRoleUri = "http://www.w3.org/2003/05/soap-envelope/role/next";

        /// <summary>
        /// Canonical SOAP 1.2 <c>none</c> role URI.
        /// </summary>
        public const string Soap12NoneRoleUri = "http://www.w3.org/2003/05/soap-envelope/role/none";

        /// <summary>
        /// Canonical SOAP 1.2 HTTP response <c>Content-Type</c> header value.
        /// </summary>
        public const string Soap12ResponseContentType = "application/soap+xml; charset=utf-8";

        /// <summary>
        /// Canonical SOAP 1.2 <c>ultimateReceiver</c> role URI.
        /// </summary>
        public const string Soap12UltimateReceiverRoleUri = "http://www.w3.org/2003/05/soap-envelope/role/ultimateReceiver";

        /// <summary>
        /// HTTP header name for SOAP 1.1 action routing.
        /// </summary>
        public const string SoapActionHeaderName = "SOAPAction";

        #endregion

        #region Properties

        /// <summary>
        /// Gets the UTF-8 bytes for the SOAP 1.1 <c>actor</c> attribute local name.
        /// </summary>
        public static ReadOnlySpan<byte> ActorLocalNameUtf8 => "actor"u8;

        /// <summary>
        /// Gets the UTF-8 bytes for the SOAP <c>Body</c> element local name.
        /// </summary>
        public static ReadOnlySpan<byte> BodyLocalNameUtf8 => "Body"u8;

        /// <summary>
        /// Gets the UTF-8 bytes for the SOAP <c>encodingStyle</c> attribute local name.
        /// </summary>
        public static ReadOnlySpan<byte> EncodingStyleLocalNameUtf8 => "encodingStyle"u8;

        /// <summary>
        /// Gets the UTF-8 bytes for the SOAP <c>Envelope</c> element local name.
        /// </summary>
        public static ReadOnlySpan<byte> EnvelopeLocalNameUtf8 => "Envelope"u8;

        /// <summary>
        /// Gets the UTF-8 bytes for the canonical SOAP envelope namespace prefix (<c>s</c>).
        /// </summary>
        public static ReadOnlySpan<byte> EnvelopePrefixUtf8 => "s"u8;

        /// <summary>
        /// Gets the UTF-8 bytes for the SOAP <c>Fault</c> element local name.
        /// </summary>
        public static ReadOnlySpan<byte> FaultLocalNameUtf8 => "Fault"u8;

        /// <summary>
        /// Gets the UTF-8 bytes for the SOAP <c>Header</c> element local name.
        /// </summary>
        public static ReadOnlySpan<byte> HeaderLocalNameUtf8 => "Header"u8;

        /// <summary>
        /// Gets the UTF-8 bytes for the SOAP <c>mustUnderstand</c> attribute local name.
        /// </summary>
        public static ReadOnlySpan<byte> MustUnderstandLocalNameUtf8 => "mustUnderstand"u8;

        /// <summary>
        /// Gets the UTF-8 bytes for the SOAP 1.2 <c>NotUnderstood</c> header element local name.
        /// </summary>
        public static ReadOnlySpan<byte> NotUnderstoodLocalNameUtf8 => "NotUnderstood"u8;

        /// <summary>
        /// Gets the UTF-8 bytes for the SOAP 1.2 <c>role</c> attribute local name.
        /// </summary>
        public static ReadOnlySpan<byte> RoleLocalNameUtf8 => "role"u8;

        /// <summary>
        /// Gets the UTF-8 bytes for the SOAP 1.1 <c>text/xml</c> media type.
        /// </summary>
        public static ReadOnlySpan<byte> Soap11MediaTypeUtf8 => "text/xml"u8;

        /// <summary>
        /// Gets the UTF-8 bytes for the SOAP 1.1 envelope namespace URI.
        /// </summary>
        public static ReadOnlySpan<byte> Soap11NamespaceUtf8 => "http://schemas.xmlsoap.org/soap/envelope/"u8;

        /// <summary>
        /// Gets the UTF-8 bytes for the SOAP 1.2 <c>application/soap+xml</c> media type.
        /// </summary>
        public static ReadOnlySpan<byte> Soap12MediaTypeUtf8 => "application/soap+xml"u8;

        /// <summary>
        /// Gets the UTF-8 bytes for the SOAP 1.2 envelope namespace URI.
        /// </summary>
        public static ReadOnlySpan<byte> Soap12NamespaceUtf8 => "http://www.w3.org/2003/05/soap-envelope"u8;

        #endregion

    }

}
