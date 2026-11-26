namespace FastSoap.Metadata
{

    /// <summary>
    /// Identifies the XML Schema type category supported by the FastSoap metadata subset.
    /// </summary>
    public enum SoapSchemaTypeKind
    {
        /// <summary>
        /// Built-in W3C XML Schema primitive simple type (<c>http://www.w3.org/2001/XMLSchema</c>).
        /// </summary>
        Primitive = 0,

        /// <summary>
        /// Complex type composed of an ordered <c>xs:sequence</c> of element members.
        /// </summary>
        Complex = 1,

        /// <summary>
        /// Simple type restriction over <c>xs:string</c> with explicit <c>xs:enumeration</c> facets.
        /// </summary>
        Enum = 2
    }

}
