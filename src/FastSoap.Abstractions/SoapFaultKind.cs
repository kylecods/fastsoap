namespace FastSoap.Abstractions
{

    /// <summary>
    /// Normalizes standard SOAP 1.1 and SOAP 1.2 fault classifications across protocol versions.
    /// </summary>
    public enum SoapFaultKind
    {
        /// <summary>
        /// Indicates the message was malformed or contained invalid client input
        /// (SOAP 1.1 <c>Client</c>; SOAP 1.2 <c>Sender</c>).
        /// </summary>
        Sender = 0,

        /// <summary>
        /// Indicates an error occurred on the server while processing a well-formed request
        /// (SOAP 1.1 <c>Server</c>; SOAP 1.2 <c>Receiver</c>).
        /// </summary>
        Receiver = 1,

        /// <summary>
        /// Indicates the envelope root element used an unrecognized SOAP envelope namespace
        /// (<c>VersionMismatch</c>).
        /// </summary>
        VersionMismatch = 2,

        /// <summary>
        /// Indicates a mandatory SOAP header targeted at this node was not understood
        /// (<c>MustUnderstand</c>).
        /// </summary>
        MustUnderstand = 3
    }

}
