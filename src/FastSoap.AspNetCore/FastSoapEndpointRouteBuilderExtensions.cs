using System;
using System.Buffers;
using System.Collections.Generic;

using FastSoap.Abstractions;
using FastSoap.AspNetCore.Internal;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FastSoap.AspNetCore
{

    /// <summary>
    /// Provides ASP.NET Core endpoint routing extensions for mapping FastSoap endpoints using plain
    /// <see cref="RequestDelegate"/> pipelines without reflection or minimal API parameter binding.
    /// </summary>
    /// <example>
    /// <code>
    /// app.MapSoap("/PaymentService.svc", static soap =>
    /// {
    ///     soap.MapOperation(
    ///         descriptor: PaymentContract.Transfer,
    ///         requestType: PaymentXmlContext.Default.TransferRequestWire,
    ///         responseType: PaymentXmlContext.Default.TransferResponseWire,
    ///         handler: static (wire, context, ct) => ...);
    /// }).RequireAuthorization();
    /// </code>
    /// </example>
    public static class FastSoapEndpointRouteBuilderExtensions
    {

        #region Fields

        private static readonly string[] GetMethods = [HttpMethods.Get];
        private static readonly string[] PostMethods = [HttpMethods.Post];

        #endregion

        #region Public Methods

        /// <summary>
        /// Maps a FastSoap endpoint at <paramref name="pattern"/> for SOAP HTTP POST processing and optional
        /// static WSDL/XSD GET metadata serving when <see cref="SoapEndpointOptions.EnableMetadata"/> is <see langword="true"/>.
        /// </summary>
        /// <param name="endpoints">The ASP.NET Core <see cref="IEndpointRouteBuilder"/>.</param>
        /// <param name="pattern">The route pattern for the SOAP endpoint (for example, <c>/PaymentService.svc</c>).</param>
        /// <param name="configure">An action that configures operations, options, header handlers, and metadata on <see cref="SoapEndpointBuilder"/>.</param>
        /// <returns>An <see cref="IEndpointConventionBuilder"/> covering the mapped SOAP endpoint(s) for standard ASP.NET Core conventions such as <c>.RequireAuthorization()</c>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoints"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="pattern"/> is null or whitespace.</exception>
        public static IEndpointConventionBuilder MapSoap(
            this IEndpointRouteBuilder endpoints,
            string pattern,
            Action<SoapEndpointBuilder> configure)
        {
            ArgumentNullException.ThrowIfNull(endpoints);
            ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
            ArgumentNullException.ThrowIfNull(configure);

            FastSoapEndpointDefaultsRegistry? defaultsRegistry = endpoints.ServiceProvider.GetService<FastSoapEndpointDefaultsRegistry>();
            SoapEndpointOptions defaultOptions = defaultsRegistry is not null
                ? defaultsRegistry.CreateDefaultOptions()
                : new SoapEndpointOptions();

            ArrayPool<byte>? bufferPool = endpoints.ServiceProvider.GetService<ArrayPool<byte>>();

            SoapEndpointBuilder soapBuilder = new SoapEndpointBuilder(defaultOptions, bufferPool);
            configure(soapBuilder);

            BuiltSoapEndpoint builtEndpoint = soapBuilder.Build();

            ILoggerFactory? loggerFactory = endpoints.ServiceProvider.GetService<ILoggerFactory>();
            ILogger logger = loggerFactory is not null
                ? loggerFactory.CreateLogger(typeof(SoapHttpEndpointHandler))
                : NullLogger.Instance;

            SoapHttpEndpointHandler handler = new SoapHttpEndpointHandler(pattern, builtEndpoint, logger);

            RequestDelegate postDelegate = handler.HandlePostAsync;
            IEndpointConventionBuilder postEndpoint = endpoints
                .MapMethods(pattern, PostMethods, postDelegate)
                .WithDisplayName($"FastSoap POST {pattern}");

            List<IEndpointConventionBuilder> builders = new List<IEndpointConventionBuilder>(2)
            {
                postEndpoint
            };

            if (builtEndpoint.Options.EnableMetadata)
            {
                RequestDelegate getDelegate = handler.HandleMetadataGetAsync;
                IEndpointConventionBuilder metadataEndpoint = endpoints
                    .MapMethods(pattern, GetMethods, getDelegate)
                    .WithDisplayName($"FastSoap Metadata GET {pattern}");

                builders.Add(metadataEndpoint);
            }

            if (logger.IsEnabled(LogLevel.Information))
            {
                FastSoapLogMessages.EndpointMapped(
                    logger,
                    pattern,
                    builtEndpoint.OperationCount,
                    builtEndpoint.Options.EnabledVersions.ToString(),
                    builtEndpoint.Options.EnableMetadata);
            }

            return new CompositeEndpointConventionBuilder(builders);
        }

        #endregion

        #region Nested Types

        private sealed class CompositeEndpointConventionBuilder : IEndpointConventionBuilder
        {

            #region Fields

            private readonly IReadOnlyList<IEndpointConventionBuilder> _builders;

            #endregion

            #region Constructors

            internal CompositeEndpointConventionBuilder(IReadOnlyList<IEndpointConventionBuilder> builders)
            {
                _builders = builders;
            }

            #endregion

            #region Public Methods

            public void Add(Action<EndpointBuilder> convention)
            {
                ArgumentNullException.ThrowIfNull(convention);

                for (int index = 0; index < _builders.Count; index++)
                {
                    _builders[index].Add(convention);
                }
            }

            public void Finally(Action<EndpointBuilder> finallyConvention)
            {
                ArgumentNullException.ThrowIfNull(finallyConvention);

                for (int index = 0; index < _builders.Count; index++)
                {
                    _builders[index].Finally(finallyConvention);
                }
            }

            #endregion

        }

        #endregion

    }

}
