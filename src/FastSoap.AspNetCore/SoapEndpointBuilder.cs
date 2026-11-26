using System;
using System.Buffers;
using System.Collections.Generic;

using CloudNimble.Text.Xml.Serialization;

using FastSoap.Abstractions;
using FastSoap.Core.Dispatching;
using FastSoap.Metadata;

namespace FastSoap.AspNetCore
{

    /// <summary>
    /// Configures operations, protocol options, explicit SOAP header handlers, and optional static WSDL/XSD metadata
    /// for a single FastSoap ASP.NET Core endpoint before startup tables are frozen.
    /// </summary>
    /// <example>
    /// <code>
    /// app.MapSoap("/PaymentService.svc", static soap =>
    /// {
    ///     soap.ConfigureOptions(options => options.EnabledVersions = SoapVersionFlags.Both);
    ///     soap.MapOperation(
    ///         descriptor: PaymentContract.Transfer,
    ///         requestType: PaymentXmlContext.Default.TransferRequestWire,
    ///         responseType: PaymentXmlContext.Default.TransferResponseWire,
    ///         handler: static (wire, ctx, ct) => ...);
    /// });
    /// </code>
    /// </example>
    public sealed class SoapEndpointBuilder
    {

        #region Fields

        private ArrayPool<byte> _bufferPool;
        private readonly List<ISoapHeaderHandler> _headerHandlers = new List<ISoapHeaderHandler>();
        private SoapStaticMetadataProvider? _metadataProvider;
        private int _operationCount;
        private readonly SoapOperationTableBuilder _operationTableBuilder = new SoapOperationTableBuilder();
        private readonly SoapEndpointOptions _options;

        #endregion

        #region Constructors

        internal SoapEndpointBuilder(SoapEndpointOptions defaultOptions, ArrayPool<byte>? bufferPool)
        {
            ArgumentNullException.ThrowIfNull(defaultOptions);

            _options = defaultOptions;
            _bufferPool = bufferPool ?? ArrayPool<byte>.Shared;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Registers an explicit SOAP header handler for this endpoint.
        /// </summary>
        /// <param name="handler">The <see cref="ISoapHeaderHandler"/> instance to invoke for matching targeted headers.</param>
        /// <returns>This <see cref="SoapEndpointBuilder"/> instance for fluent chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="handler"/> is <see langword="null"/>.</exception>
        public SoapEndpointBuilder AddHeaderHandler(ISoapHeaderHandler handler)
        {
            ArgumentNullException.ThrowIfNull(handler);

            _headerHandlers.Add(handler);

            return this;
        }

        /// <summary>
        /// Configures <see cref="SoapEndpointOptions"/> specific to this endpoint.
        /// </summary>
        /// <param name="configure">An action that mutates this endpoint's options prior to startup validation.</param>
        /// <returns>This <see cref="SoapEndpointBuilder"/> instance for fluent chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
        public SoapEndpointBuilder ConfigureOptions(Action<SoapEndpointOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);

            configure(_options);

            return this;
        }

        /// <summary>
        /// Generates and attaches deterministic WSDL 1.1 and XSD metadata artifacts from an explicit
        /// <see cref="SoapServiceMetadata"/> contract and <see cref="SoapMetadataAddressPolicy"/>.
        /// </summary>
        /// <param name="serviceMetadata">The validated contract-first service metadata definition.</param>
        /// <param name="addressPolicy">The explicit public endpoint URI policy (never derived from untrusted request headers).</param>
        /// <param name="mode">Specifies whether schemas are emitted inline or as separate <c>?xsd=...</c> endpoints.</param>
        /// <returns>This <see cref="SoapEndpointBuilder"/> instance for fluent chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="serviceMetadata"/> or <paramref name="addressPolicy"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="mode"/> is undefined.</exception>
        /// <exception cref="InvalidOperationException">Thrown when metadata has already been configured for this endpoint.</exception>
        public SoapEndpointBuilder MapGeneratedMetadata(
            SoapServiceMetadata serviceMetadata,
            SoapMetadataAddressPolicy addressPolicy,
            SoapWsdlGenerationMode mode = SoapWsdlGenerationMode.ExternalSchemas)
        {
            ArgumentNullException.ThrowIfNull(serviceMetadata);
            ArgumentNullException.ThrowIfNull(addressPolicy);

            if (!Enum.IsDefined(mode))
            {
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported WSDL generation mode.");
            }

            if (_metadataProvider is not null)
            {
                throw new InvalidOperationException("Metadata has already been configured for this SOAP endpoint.");
            }

            _metadataProvider = mode == SoapWsdlGenerationMode.InlineSchemas
                ? new SoapStaticMetadataProvider(SoapWsdlGenerator.GenerateWsdlDocument(serviceMetadata, addressPolicy))
                : SoapWsdlGenerator.GenerateStaticProvider(serviceMetadata, addressPolicy, useQuerySchemaLocations: true);

            return this;
        }

        /// <summary>
        /// Registers a strongly typed SOAP operation backed by source-generated <see cref="XmlTypeInfo{T}"/> metadata.
        /// </summary>
        /// <typeparam name="TRequest">The request wire DTO type.</typeparam>
        /// <typeparam name="TResponse">The response wire DTO type.</typeparam>
        /// <param name="descriptor">The validated operation descriptor.</param>
        /// <param name="requestType">The source-generated <see cref="XmlTypeInfo{T}"/> for <typeparamref name="TRequest"/>.</param>
        /// <param name="responseType">The source-generated <see cref="XmlTypeInfo{T}"/> for <typeparamref name="TResponse"/>.</param>
        /// <param name="handler">The typed asynchronous operation handler delegate.</param>
        /// <returns>This <see cref="SoapEndpointBuilder"/> instance for fluent chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">Thrown when an operation with the same action is already registered.</exception>
        public SoapEndpointBuilder MapOperation<TRequest, TResponse>(
            SoapOperationDescriptor descriptor,
            XmlTypeInfo<TRequest> requestType,
            XmlTypeInfo<TResponse> responseType,
            SoapOperationHandler<TRequest, TResponse> handler)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            ArgumentNullException.ThrowIfNull(requestType);
            ArgumentNullException.ThrowIfNull(responseType);
            ArgumentNullException.ThrowIfNull(handler);

            _operationTableBuilder.MapOperation(descriptor, requestType, responseType, handler);
            _operationCount++;

            return this;
        }

        /// <summary>
        /// Attaches a pre-validated <see cref="SoapStaticMetadataProvider"/> for GET <c>?wsdl</c> and <c>?xsd=</c> serving
        /// when <see cref="SoapEndpointOptions.EnableMetadata"/> is <see langword="true"/>.
        /// </summary>
        /// <param name="metadataProvider">The immutable static WSDL/XSD provider.</param>
        /// <returns>This <see cref="SoapEndpointBuilder"/> instance for fluent chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="metadataProvider"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">Thrown when metadata has already been configured for this endpoint.</exception>
        public SoapEndpointBuilder MapStaticMetadata(SoapStaticMetadataProvider metadataProvider)
        {
            ArgumentNullException.ThrowIfNull(metadataProvider);

            if (_metadataProvider is not null)
            {
                throw new InvalidOperationException("Metadata has already been configured for this SOAP endpoint.");
            }

            _metadataProvider = metadataProvider;

            return this;
        }

        /// <summary>
        /// Validates and attaches raw UTF-8 WSDL 1.1 and optional named XSD schema bytes for GET <c>?wsdl</c> and <c>?xsd=</c> serving.
        /// </summary>
        /// <param name="wsdlUtf8">The raw UTF-8 WSDL 1.1 document bytes.</param>
        /// <param name="schemas">Optional named XSD schema documents.</param>
        /// <returns>This <see cref="SoapEndpointBuilder"/> instance for fluent chaining.</returns>
        /// <exception cref="InvalidOperationException">Thrown when metadata has already been configured for this endpoint.</exception>
        public SoapEndpointBuilder MapStaticMetadata(
            ReadOnlySpan<byte> wsdlUtf8,
            IEnumerable<KeyValuePair<string, byte[]>>? schemas = null)
        {
            return MapStaticMetadata(SoapStaticMetadataProvider.Create(wsdlUtf8, schemas));
        }

        #endregion

        #region Internal Methods

        internal BuiltSoapEndpoint Build()
        {
            _options.Validate();

            if (_options.EnableMetadata && _metadataProvider is null)
            {
                throw new InvalidOperationException(
                    "SoapEndpointOptions.EnableMetadata is true, but no metadata provider was configured via MapStaticMetadata or MapGeneratedMetadata.");
            }

            SoapOperationTable operations = _operationTableBuilder.Build(_options);
            SoapHeaderProcessor headerProcessor = _headerHandlers.Count == 0
                ? SoapHeaderProcessor.Empty
                : new SoapHeaderProcessor(_headerHandlers);
            SoapMessageProcessor messageProcessor = new SoapMessageProcessor(
                _options,
                operations,
                headerProcessor,
                _bufferPool);

            return new BuiltSoapEndpoint(
                _options,
                operations,
                messageProcessor,
                _metadataProvider,
                _bufferPool,
                _operationCount);
        }

        internal SoapEndpointBuilder UseBufferPool(ArrayPool<byte> pool)
        {
            ArgumentNullException.ThrowIfNull(pool);

            _bufferPool = pool;

            return this;
        }

        #endregion

    }

    internal sealed class BuiltSoapEndpoint
    {

        #region Properties

        internal ArrayPool<byte> BufferPool { get; }

        internal SoapMessageProcessor MessageProcessor { get; }

        internal SoapStaticMetadataProvider? MetadataProvider { get; }

        internal int OperationCount { get; }

        internal SoapOperationTable Operations { get; }

        internal SoapEndpointOptions Options { get; }

        #endregion

        #region Constructors

        internal BuiltSoapEndpoint(
            SoapEndpointOptions options,
            SoapOperationTable operations,
            SoapMessageProcessor messageProcessor,
            SoapStaticMetadataProvider? metadataProvider,
            ArrayPool<byte> bufferPool,
            int operationCount)
        {
            Options = options;
            Operations = operations;
            MessageProcessor = messageProcessor;
            MetadataProvider = metadataProvider;
            BufferPool = bufferPool;
            OperationCount = operationCount;
        }

        #endregion

    }

}
