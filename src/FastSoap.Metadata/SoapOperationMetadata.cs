using System;
using FastSoap.Abstractions;

namespace FastSoap.Metadata
{

    /// <summary>
    /// Binds a shared <see cref="SoapOperationDescriptor"/> to explicit global request and response
    /// <see cref="SoapSchemaElement"/> declarations for deterministic WSDL 1.1 and XSD generation.
    /// </summary>
    /// <example>
    /// <code>
    /// SoapOperationMetadata operation = new SoapOperationMetadata(
    ///     descriptor,
    ///     requestSchema,
    ///     responseSchema);
    /// </code>
    /// </example>
    /// <remarks>
    /// Validates that <see cref="RequestSchema"/> and <see cref="ResponseSchema"/> match the exact
    /// expanded qualified names declared by <see cref="Descriptor"/>.
    /// </remarks>
    public sealed class SoapOperationMetadata
    {

        #region Properties

        /// <summary>
        /// Gets the runtime operation descriptor shared with dispatch and source generation.
        /// </summary>
        public SoapOperationDescriptor Descriptor { get; }

        /// <summary>
        /// Gets the global request Body element schema declaration.
        /// </summary>
        public SoapSchemaElement RequestSchema { get; }

        /// <summary>
        /// Gets the global response Body element schema declaration.
        /// </summary>
        public SoapSchemaElement ResponseSchema { get; }

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new validated <see cref="SoapOperationMetadata"/> instance.
        /// </summary>
        /// <param name="descriptor">The runtime operation descriptor.</param>
        /// <param name="requestSchema">The global request Body element declaration matching <see cref="SoapOperationDescriptor.RequestElement"/>.</param>
        /// <param name="responseSchema">The global response Body element declaration matching <see cref="SoapOperationDescriptor.ResponseElement"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when element qualified names do not match <paramref name="descriptor"/> or are not valid global declarations.</exception>
        public SoapOperationMetadata(
            SoapOperationDescriptor descriptor,
            SoapSchemaElement requestSchema,
            SoapSchemaElement responseSchema)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            ArgumentNullException.ThrowIfNull(requestSchema);
            ArgumentNullException.ThrowIfNull(responseSchema);

            ValidateGlobalMessageElement(requestSchema, descriptor.RequestElement, nameof(requestSchema));
            ValidateGlobalMessageElement(responseSchema, descriptor.ResponseElement, nameof(responseSchema));

            Descriptor = descriptor;
            RequestSchema = requestSchema;
            ResponseSchema = responseSchema;
        }

        #endregion

        #region Private Methods

        private static void ValidateGlobalMessageElement(
            SoapSchemaElement element,
            SoapQName expectedQName,
            string paramName)
        {
            if (string.IsNullOrWhiteSpace(element.NamespaceUri))
            {
                throw new ArgumentException("Message schema element must declare a qualified namespace URI.", paramName);
            }

            if (element.MinOccurs != 1 || element.MaxOccurs != 1)
            {
                throw new ArgumentException("Global message schema element must have minOccurs=1 and maxOccurs=1.", paramName);
            }

            if (!string.Equals(element.NamespaceUri, expectedQName.NamespaceUri, StringComparison.Ordinal)
                || !string.Equals(element.Name, expectedQName.LocalName, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Schema element '{element.QName}' does not match operation descriptor element '{expectedQName}'.",
                    paramName);
            }
        }

        #endregion

    }

}
