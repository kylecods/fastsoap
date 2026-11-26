namespace FastSoap.Abstractions
{

    /// <summary>
    /// Identifies the SOAP messaging protocol version for an envelope, fault, or call context.
    /// </summary>
    public enum SoapVersion
    {
        /// <summary>
        /// SOAP 1.1 (<c>http://schemas.xmlsoap.org/soap/envelope/</c>, <c>text/xml</c>).
        /// </summary>
        Soap11 = 0,

        /// <summary>
        /// SOAP 1.2 (<c>http://www.w3.org/2003/05/soap-envelope</c>, <c>application/soap+xml</c>).
        /// </summary>
        Soap12 = 1
    }

}
