using System;

namespace FastSoap.Abstractions
{

    /// <summary>
    /// Explicitly associates a <see cref="SoapServiceAttribute"/> contract or <see cref="SoapOperationAttribute"/>
    /// method with its authored XML serializer context and explicit wire DTO types per ADR-003.
    /// </summary>
    /// <example>
    /// <code>
    /// [SoapService(Namespace = "urn:payments")]
    /// [SoapWireContract(typeof(PaymentXmlContext))]
    /// public interface IPaymentService
    /// {
    ///     [SoapOperation(Action = "urn:payments/IPaymentService/Transfer")]
    ///     [SoapWireContract(
    ///         RequestType = typeof(TransferRequestWire),
    ///         RequestMemberName = nameof(TransferRequestWire.Request),
    ///         ResponseType = typeof(TransferResponseWire),
    ///         ResponseMemberName = nameof(TransferResponseWire.Result))]
    ///     Task&lt;TransferResult&gt; TransferAsync(TransferRequest request, CancellationToken cancellationToken);
    /// }
    /// </code>
    /// </example>
    /// <remarks>
    /// This attribute is read solely at compile time by Roslyn incremental source generators and never triggers runtime reflection.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Class | AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class SoapWireContractAttribute : Attribute
    {

        #region Properties

        /// <summary>
        /// Gets or sets the authored <c>XmlSerializerContext</c> subclass containing generated metadata for the wire DTOs.
        /// </summary>
        public Type? ContextType { get; set; }

        /// <summary>
        /// Gets or sets the property name on <see cref="RequestType"/> that wraps the service method's input parameter,
        /// or <see langword="null"/> when <see cref="RequestType"/> is passed directly to the service method.
        /// </summary>
        public string? RequestMemberName { get; set; }

        /// <summary>
        /// Gets or sets the explicit wire DTO type representing the SOAP request Body child element.
        /// </summary>
        public Type? RequestType { get; set; }

        /// <summary>
        /// Gets or sets the property name on <see cref="ContextType"/> exposing the generated type info for <see cref="RequestType"/>,
        /// when different from <c>RequestType.Name</c>.
        /// </summary>
        public string? RequestTypeInfoPropertyName { get; set; }

        /// <summary>
        /// Gets or sets the property name on <see cref="ResponseType"/> that wraps the service method's return value,
        /// or <see langword="null"/> when the service method returns <see cref="ResponseType"/> directly.
        /// </summary>
        public string? ResponseMemberName { get; set; }

        /// <summary>
        /// Gets or sets the explicit wire DTO type representing the SOAP response Body child element.
        /// </summary>
        public Type? ResponseType { get; set; }

        /// <summary>
        /// Gets or sets the property name on <see cref="ContextType"/> exposing the generated type info for <see cref="ResponseType"/>,
        /// when different from <c>ResponseType.Name</c>.
        /// </summary>
        public string? ResponseTypeInfoPropertyName { get; set; }

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of <see cref="SoapWireContractAttribute"/>.
        /// </summary>
        public SoapWireContractAttribute()
        {
        }

        /// <summary>
        /// Initializes a new instance of <see cref="SoapWireContractAttribute"/> bound to a specific XML serializer context type.
        /// </summary>
        /// <param name="contextType">The authored <c>XmlSerializerContext</c> subclass type.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="contextType"/> is <see langword="null"/>.</exception>
        public SoapWireContractAttribute(Type contextType)
        {
            ArgumentNullException.ThrowIfNull(contextType);

            ContextType = contextType;
        }

        #endregion

    }

}
