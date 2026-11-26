using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

using CoreWCF;
using CoreWCF.Channels;
using CoreWCF.Configuration;
using CoreWCF.Dispatcher;

using FastSoap.Abstractions;
using FastSoap.AspNetCore;
using FastSoap.Benchmarks.Contracts;
using FastSoap.Benchmarks.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using SoapCore;

namespace FastSoap.Benchmarks.HttpLoad
{

    /// <summary>
    /// Hosts an isolated Kestrel benchmark server in <c>Production</c> environment with logging cleared
    /// for one target implementation (<c>fastsoap-manual</c>, <c>fastsoap-generated</c>, <c>corewcf-dcs</c>,
    /// <c>corewcf-xmlser</c>, or <c>soapcore-xmlser</c>).
    /// </summary>
    public static class ServerHost
    {

        #region Public Methods

        /// <summary>
        /// Runs the specified server variant on Kestrel loopback port <c>0</c>, prints readiness metadata,
        /// and waits until <c>Ctrl+C</c> or process termination.
        /// </summary>
        /// <param name="variant">The server variant identifier.</param>
        /// <returns>Zero on clean shutdown; non-zero on invalid variant or verification failure.</returns>
        public static async Task<int> RunServerAsync(string variant)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(variant);

            WebApplicationOptions appOptions = new WebApplicationOptions
            {
                EnvironmentName = Environments.Production
            };

            bool isFastSoap = string.Equals(variant, "fastsoap-manual", StringComparison.Ordinal) ||
                string.Equals(variant, "fastsoap-generated", StringComparison.Ordinal);

            WebApplicationBuilder builder = isFastSoap
                ? WebApplication.CreateSlimBuilder(appOptions)
                : WebApplication.CreateBuilder(appOptions);
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(static options =>
            {
                options.Listen(IPAddress.Loopback, 0);
            });

            List<ServiceHostBase>? coreWcfHosts = null;

            switch (variant)
            {
                case "fastsoap-manual":
                    builder.Services.AddFastSoap(static options =>
                    {
                        options.EnabledVersions = SoapVersionFlags.Soap11;
                    });
                    break;

                case "fastsoap-generated":
                    builder.Services.AddFastSoap(static options =>
                    {
                        options.EnabledVersions = SoapVersionFlags.Soap11;
                    });
                    builder.Services.AddSingleton<IBenchmarkEchoService, BenchmarkEchoService>();
                    builder.Services.AddSingleton<IBenchmarkPaymentService, BenchmarkPaymentService>();
                    break;

                case "corewcf-dcs":
                    builder.Services.AddServiceModelServices();
                    builder.Services.AddSingleton<CoreWcfEchoDcsService>();
                    builder.Services.AddSingleton<CoreWcfPaymentDcsService>();
                    coreWcfHosts = [];
                    break;

                case "corewcf-xmlser":
                    builder.Services.AddServiceModelServices();
                    builder.Services.AddSingleton<CoreWcfEchoXmlService>();
                    builder.Services.AddSingleton<CoreWcfPaymentXmlService>();
                    coreWcfHosts = [];
                    break;

                case "soapcore-xmlser":
                    builder.Services.AddSoapCore();
                    builder.Services.AddSingleton<ISoapCoreEchoService, SoapCoreEchoService>();
                    builder.Services.AddSingleton<ISoapCorePaymentService, SoapCorePaymentService>();
                    break;

                default:
                    Console.Error.WriteLine($"Unknown server variant '{variant}'.");

                    return 1;
            }

            WebApplication app = builder.Build();

            app.MapGet("/__bench/stats", static (HttpContext context) =>
            {
                long allocated = GC.GetTotalAllocatedBytes(precise: false);
                int gen0 = GC.CollectionCount(0);
                int gen1 = GC.CollectionCount(1);
                int gen2 = GC.CollectionCount(2);
                long workingSet = Environment.WorkingSet;

                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.Headers.ContentType = "text/plain; charset=utf-8";

                string payload = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{allocated}|{gen0}|{gen1}|{gen2}|{workingSet}");

                return context.Response.WriteAsync(payload, context.RequestAborted);
            });

            switch (variant)
            {
                case "fastsoap-manual":
                    MapFastSoapManual(app);
                    break;

                case "fastsoap-generated":
                    app.MapBenchmarkEchoService("/echo.svc");
                    app.MapBenchmarkPaymentService("/payments.svc");
                    break;

                case "corewcf-dcs":
                    MapCoreWcfDcs(app, coreWcfHosts!);
                    break;

                case "corewcf-xmlser":
                    MapCoreWcfXml(app, coreWcfHosts!);
                    break;

                case "soapcore-xmlser":
                    MapSoapCoreXml(app);
                    break;
            }

            await app.StartAsync().ConfigureAwait(false);

            try
            {
                if (coreWcfHosts is not null)
                {
                    VerifyCoreWcfGeneratedInvokers(coreWcfHosts);
                }

                IServer server = app.Services.GetRequiredService<IServer>();
                IServerAddressesFeature addresses = server.Features.Get<IServerAddressesFeature>()
                    ?? throw new InvalidOperationException("Kestrel failed to expose listening address.");
                string listenUrl = addresses.Addresses.First();

                Console.WriteLine($"READY|{variant}|{listenUrl}|{app.Environment.EnvironmentName}");
                Console.Out.Flush();

                await app.WaitForShutdownAsync().ConfigureAwait(false);
            }
            finally
            {
                await app.StopAsync().ConfigureAwait(false);
                await app.DisposeAsync().ConfigureAwait(false);
            }

            return 0;
        }

        #endregion

        #region Private Methods

        private static void MapCoreWcfDcs(WebApplication app, List<ServiceHostBase> hosts)
        {
            app.UseServiceModel(serviceBuilder =>
            {
                BasicHttpBinding binding = new BasicHttpBinding(BasicHttpSecurityMode.None);

                serviceBuilder.AddService<CoreWcfEchoDcsService>();
                serviceBuilder.AddService<CoreWcfPaymentDcsService>();

                serviceBuilder.AddServiceEndpoint<CoreWcfEchoDcsService, ICoreWcfEchoDcsService>(binding, "/echo.svc");
                serviceBuilder.AddServiceEndpoint<CoreWcfPaymentDcsService, ICoreWcfPaymentDcsService>(binding, "/payments.svc");

                serviceBuilder.ConfigureAllServiceHostBase(host =>
                {
                    if (!hosts.Contains(host))
                    {
                        hosts.Add(host);
                    }
                });
            });
        }

        private static void MapCoreWcfXml(WebApplication app, List<ServiceHostBase> hosts)
        {
            app.UseServiceModel(serviceBuilder =>
            {
                BasicHttpBinding binding = new BasicHttpBinding(BasicHttpSecurityMode.None);

                serviceBuilder.AddService<CoreWcfEchoXmlService>();
                serviceBuilder.AddService<CoreWcfPaymentXmlService>();

                serviceBuilder.AddServiceEndpoint<CoreWcfEchoXmlService, ICoreWcfEchoXmlService>(binding, "/echo.svc");
                serviceBuilder.AddServiceEndpoint<CoreWcfPaymentXmlService, ICoreWcfPaymentXmlService>(binding, "/payments.svc");

                serviceBuilder.ConfigureAllServiceHostBase(host =>
                {
                    if (!hosts.Contains(host))
                    {
                        hosts.Add(host);
                    }
                });
            });
        }

        private static void MapFastSoapManual(WebApplication app)
        {
            SoapOperationDescriptor echoDescriptor = BenchmarkPayloads.CreateManualEchoDescriptor();
            SoapOperationDescriptor transferDescriptor = BenchmarkPayloads.CreateManualTransferDescriptor();

            app.MapSoap("/echo.svc", soap =>
            {
                soap.MapOperation(
                    descriptor: echoDescriptor,
                    requestType: BenchmarkPayloads.SharedXmlContext.EchoStringRequestWire,
                    responseType: BenchmarkPayloads.SharedXmlContext.EchoStringResponseWire,
                    handler: static (wire, _, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();

                        return ValueTask.FromResult(new EchoStringResponseWire
                        {
                            EchoStringResult = wire.Echo
                        });
                    });
            });

            app.MapSoap("/payments.svc", soap =>
            {
                soap.MapOperation(
                    descriptor: transferDescriptor,
                    requestType: BenchmarkPayloads.SharedXmlContext.TransferRequestWire,
                    responseType: BenchmarkPayloads.SharedXmlContext.TransferResponseWire,
                    handler: static (wire, _, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();

                        return ValueTask.FromResult(new TransferResponseWire
                        {
                            TransferResult = BenchmarkPayloads.ExecuteTransfer(wire.Request)
                        });
                    });
            });
        }

        private static void MapSoapCoreXml(WebApplication app)
        {
            IEndpointRouteBuilder endpoints = app;

            endpoints.UseSoapEndpoint<ISoapCoreEchoService>(static options =>
            {
                options.Path = "/echo.svc";
                options.EncoderOptions = [new SoapEncoderOptions()];
                options.SoapSerializer = SoapSerializer.XmlSerializer;
                options.OmitXmlDeclaration = true;
                options.IndentXml = false;
            });

            endpoints.UseSoapEndpoint<ISoapCorePaymentService>(static options =>
            {
                options.Path = "/payments.svc";
                options.EncoderOptions = [new SoapEncoderOptions()];
                options.SoapSerializer = SoapSerializer.XmlSerializer;
                options.OmitXmlDeclaration = true;
                options.IndentXml = false;
            });
        }

        private static void VerifyCoreWcfGeneratedInvokers(List<ServiceHostBase> hosts)
        {
            int verifiedCount = 0;

            foreach (ServiceHostBase host in hosts)
            {
                foreach (ChannelDispatcherBase dispatcherBase in host.ChannelDispatchers)
                {
                    if (dispatcherBase is not ChannelDispatcher channelDispatcher)
                    {
                        continue;
                    }

                    foreach (EndpointDispatcher endpointDispatcher in channelDispatcher.Endpoints)
                    {
                        string path = endpointDispatcher.EndpointAddress.Uri.AbsolutePath;
                        foreach (DispatchOperation op in endpointDispatcher.DispatchRuntime.Operations)
                        {
                            string invokerName = op.Invoker?.GetType().FullName ?? "<null>";
                            string formatterName = op.Formatter?.GetType().FullName ?? "<null>";

                            Console.WriteLine($"COREWCF_OP|{path}|{op.Name}|{invokerName}|{formatterName}");

                            if (!invokerName.Contains("OperationInvoker", StringComparison.Ordinal))
                            {
                                throw new InvalidOperationException(
                                    $"Expected generated CoreWCF OperationInvoker for '{path}' / '{op.Name}', but found '{invokerName}'.");
                            }

                            verifiedCount++;
                        }
                    }
                }
            }

            if (verifiedCount < 2)
            {
                throw new InvalidOperationException($"Expected at least 2 CoreWCF operations verified, found {verifiedCount}.");
            }
        }

        #endregion

    }

}
