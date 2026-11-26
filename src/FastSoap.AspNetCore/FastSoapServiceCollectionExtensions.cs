using System;
using System.Collections.Generic;

using FastSoap.Abstractions;

using Microsoft.Extensions.DependencyInjection;

namespace FastSoap.AspNetCore
{

    /// <summary>
    /// Provides dependency injection registration extensions for FastSoap ASP.NET Core endpoints.
    /// </summary>
    /// <example>
    /// <code>
    /// WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args);
    /// builder.Services.AddFastSoap(options =>
    /// {
    ///     options.EnabledVersions = SoapVersionFlags.Both;
    /// });
    /// </code>
    /// </example>
    public static class FastSoapServiceCollectionExtensions
    {

        #region Public Methods

        /// <summary>
        /// Registers FastSoap hosting defaults with the dependency injection container.
        /// </summary>
        /// <param name="services">The application <see cref="IServiceCollection"/>.</param>
        /// <returns>The updated <see cref="IServiceCollection"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is <see langword="null"/>.</exception>
        public static IServiceCollection AddFastSoap(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            EnsureDefaultsContainerRegistered(services);

            return services;
        }

        /// <summary>
        /// Registers FastSoap hosting defaults with an endpoint options configuration callback.
        /// </summary>
        /// <param name="services">The application <see cref="IServiceCollection"/>.</param>
        /// <param name="configureOptions">A callback configuring default <see cref="SoapEndpointOptions"/> for mapped endpoints.</param>
        /// <returns>The updated <see cref="IServiceCollection"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="configureOptions"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the configured default options are invalid.</exception>
        public static IServiceCollection AddFastSoap(
            this IServiceCollection services,
            Action<SoapEndpointOptions> configureOptions)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configureOptions);

            SoapEndpointOptions probe = new SoapEndpointOptions();
            configureOptions(probe);
            probe.Validate();

            FastSoapEndpointDefaultsRegistry registry = EnsureDefaultsContainerRegistered(services);
            registry.AddConfiguration(configureOptions);

            return services;
        }

        #endregion

        #region Private Methods

        private static FastSoapEndpointDefaultsRegistry EnsureDefaultsContainerRegistered(IServiceCollection services)
        {
            for (int index = 0; index < services.Count; index++)
            {
                ServiceDescriptor descriptor = services[index];
                if (descriptor.ServiceType == typeof(FastSoapEndpointDefaultsRegistry) &&
                    descriptor.ImplementationInstance is FastSoapEndpointDefaultsRegistry existing)
                {
                    return existing;
                }
            }

            FastSoapEndpointDefaultsRegistry created = new FastSoapEndpointDefaultsRegistry();
            services.AddSingleton(created);

            return created;
        }

        #endregion

    }

    internal sealed class FastSoapEndpointDefaultsRegistry
    {

        #region Fields

        private readonly List<Action<SoapEndpointOptions>> _configurators = new List<Action<SoapEndpointOptions>>();

        #endregion

        #region Internal Methods

        internal void AddConfiguration(Action<SoapEndpointOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);

            _configurators.Add(configure);
        }

        internal SoapEndpointOptions CreateDefaultOptions()
        {
            SoapEndpointOptions options = new SoapEndpointOptions();
            for (int index = 0; index < _configurators.Count; index++)
            {
                _configurators[index](options);
            }

            options.Validate();

            return options;
        }

        #endregion

    }

}
