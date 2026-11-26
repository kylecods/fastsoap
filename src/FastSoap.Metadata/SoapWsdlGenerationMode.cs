namespace FastSoap.Metadata
{

    /// <summary>
    /// Controls how <see cref="SoapWsdlGenerator"/> emits XSD schema definitions inside WSDL 1.1.
    /// </summary>
    public enum SoapWsdlGenerationMode
    {
        /// <summary>
        /// Embeds all per-namespace <c>xs:schema</c> blocks directly inside <c>wsdl:types</c> in a single WSDL 1.1 document.
        /// </summary>
        InlineSchemas = 0,

        /// <summary>
        /// Emits <c>xs:import</c> references inside <c>wsdl:types</c> pointing to separate per-namespace XSD documents.
        /// </summary>
        ExternalSchemas = 1
    }

}
