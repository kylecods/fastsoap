using System;

namespace FastSoap.Abstractions
{

    /// <summary>
    /// Marks a service contract method as an explicit SOAP operation for compile-time dispatch generation.
    /// </summary>
    /// <example>
    /// <code>
    /// [SoapOperation(Action = "urn:payments/IPaymentService/Transfer")]
    /// Task&lt;TransferResult&gt; TransferAsync(TransferRequest request, CancellationToken cancellationToken);
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class SoapOperationAttribute : Attribute
    {

        #region Properties

        /// <summary>
        /// Gets or sets the SOAP action URI (<c>SOAPAction</c> in SOAP 1.1 or media-type <c>action</c> in SOAP 1.2).
        /// </summary>
        public string? Action { get; set; }

        /// <summary>
        /// Gets or sets the logical operation name when different from the CLR method name.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Gets or sets the optional SOAP reply action URI.
        /// </summary>
        public string? ReplyAction { get; set; }

        /// <summary>
        /// Gets or sets the expected request wrapper element local name.
        /// </summary>
        public string? RequestName { get; set; }

        /// <summary>
        /// Gets or sets the expected response wrapper element local name.
        /// </summary>
        public string? ResponseName { get; set; }

        #endregion

    }

}
