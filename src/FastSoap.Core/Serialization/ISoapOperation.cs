using CloudNimble.Text.Xml;

using FastSoap.Abstractions;

namespace FastSoap.Core.Serialization
{

    /// <summary>
    /// Represents a registered SOAP operation capable of synchronously validating and deserializing
    /// its Body payload element and producing a non-generic <see cref="PreparedSoapCall"/>.
    /// </summary>
    /// <remarks>
    /// Implementations keep <see cref="Utf8XmlReader"/> strictly synchronous during <see cref="Prepare"/>
    /// before crossing asynchronous boundaries in <see cref="PreparedSoapCall"/>.
    /// </remarks>
    public interface ISoapOperation
    {

        #region Properties

        /// <summary>
        /// Gets the immutable operation contract descriptor.
        /// </summary>
        SoapOperationDescriptor Descriptor { get; }

        #endregion

        #region Public Methods

        /// <summary>
        /// Synchronously validates the current Body child element and deserializes the typed request payload.
        /// </summary>
        /// <param name="reader">The <see cref="Utf8XmlReader"/> positioned on the Body child start element.</param>
        /// <param name="context">The constructed per-request <see cref="SoapCallContext"/>.</param>
        /// <returns>A non-generic <see cref="PreparedSoapCall"/> ready for async execution and response staging.</returns>
        PreparedSoapCall Prepare(ref Utf8XmlReader reader, SoapCallContext context);

        #endregion

    }

}
