using System;

namespace FastSoap.Abstractions
{

    /// <summary>
    /// Marks an interface or service class as a FastSoap service contract for compile-time source generation.
    /// </summary>
    /// <example>
    /// <code>
    /// [SoapService(Name = "PaymentService", Namespace = "urn:payments")]
    /// public interface IPaymentService
    /// {
    /// }
    /// </code>
    /// </example>
    /// <remarks>
    /// Attributes in FastSoap describe contract metadata only and never trigger runtime reflection discovery.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class SoapServiceAttribute : Attribute
    {

        #region Properties

        /// <summary>
        /// Gets or sets the logical SOAP service contract name.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Gets or sets the XML target namespace URI for the service contract.
        /// </summary>
        public string? Namespace { get; set; }

        #endregion

    }

}
