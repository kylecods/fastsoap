using System;

namespace FastSoap.Abstractions
{

    /// <summary>
    /// Specifies the enabled SOAP protocol versions on an endpoint.
    /// </summary>
    [Flags]
    public enum SoapVersionFlags
    {
        /// <summary>
        /// No SOAP protocol versions enabled (invalid for active endpoint configuration).
        /// </summary>
        None = 0,

        /// <summary>
        /// Enables SOAP 1.1 over HTTP (<c>text/xml</c> and <c>SOAPAction</c>).
        /// </summary>
        Soap11 = 1,

        /// <summary>
        /// Enables SOAP 1.2 over HTTP (<c>application/soap+xml</c> without WS-Addressing).
        /// </summary>
        Soap12 = 2,

        /// <summary>
        /// Enables both SOAP 1.1 and SOAP 1.2 on the endpoint.
        /// </summary>
        Both = Soap11 | Soap12
    }

}
