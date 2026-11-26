namespace FastSoap.Metadata
{

    /// <summary>
    /// Identifies the XML metadata artifact kind stored in a <see cref="SoapMetadataDocument"/>.
    /// </summary>
    public enum SoapMetadataDocumentKind
    {
        /// <summary>
        /// WSDL 1.1 service description document (<c>http://schemas.xmlsoap.org/wsdl/</c>).
        /// </summary>
        Wsdl = 0,

        /// <summary>
        /// XML Schema (XSD 1.0) document (<c>http://www.w3.org/2001/XMLSchema</c>).
        /// </summary>
        XmlSchema = 1
    }

}
