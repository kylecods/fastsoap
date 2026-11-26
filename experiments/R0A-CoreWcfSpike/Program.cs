using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using CoreWCF;
using CoreWCF.Channels;
using CoreWCF.Configuration;
using CoreWCF.Description;
using CoreWCF.Dispatcher;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace R0A.CoreWcfSpike
{
    /// <summary>
    /// Isolated CoreWCF 1.9.2 research spike hosting Echo and nested Transfer (urn:payments) contracts
    /// across baseline, custom formatter, and custom encoder seams.
    /// </summary>
    public static class Program
    {
        #region Fields

        private const string EchoRequestEnvelope =
            """
            <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/">
              <s:Body>
                <Echo xmlns="urn:echo">
                  <message>hello-fastsoap</message>
                </Echo>
              </s:Body>
            </s:Envelope>
            """;

        private const string TransferRequestEnvelope =
            """
            <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/">
              <s:Body>
                <Transfer xmlns="urn:payments">
                  <request>
                    <AccountNumber>123456</AccountNumber>
                    <Amount>100.50</Amount>
                  </request>
                </Transfer>
              </s:Body>
            </s:Envelope>
            """;

        #endregion

        #region Public Methods

        /// <summary>
        /// Entry point for the R0-A CoreWCF spike.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Zero on success; non-zero on verification failure.</returns>
        public static async Task<int> Main(string[] args)
        {
            FormatterSeamMetrics formatterMetrics = new FormatterSeamMetrics();
            EncoderSeamMetrics encoderMetrics = new EncoderSeamMetrics();

            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(static options =>
            {
                options.Listen(IPAddress.Loopback, 0);
            });

            builder.Services.AddServiceModelServices();
            builder.Services.AddSingleton<EchoService>();
            builder.Services.AddSingleton<PaymentService>();

            WebApplication app = builder.Build();
            List<ServiceHostBase> serviceHosts = [];

            app.UseServiceModel(serviceBuilder =>
            {
                BasicHttpBinding baselineBinding = new BasicHttpBinding(BasicHttpSecurityMode.None);
                BasicHttpBinding customFormatterBinding = new BasicHttpBinding(BasicHttpSecurityMode.None);
                CustomBinding customEncoderBinding = new CustomBinding(
                    new InstrumentedSoap11EncodingBindingElement(encoderMetrics),
                    new HttpTransportBindingElement());

                serviceBuilder.AddService<EchoService>();
                serviceBuilder.AddService<PaymentService>();

                serviceBuilder.AddServiceEndpoint<EchoService, IEchoService>(baselineBinding, "/baseline/echo.svc");
                serviceBuilder.AddServiceEndpoint<PaymentService, IPaymentService>(baselineBinding, "/baseline/payments.svc");

                serviceBuilder.AddServiceEndpoint<EchoService, IEchoService>(customFormatterBinding, "/custom-formatter/echo.svc");
                serviceBuilder.AddServiceEndpoint<PaymentService, IPaymentService>(customFormatterBinding, "/custom-formatter/payments.svc");

                serviceBuilder.AddServiceEndpoint<EchoService, IEchoService>(customEncoderBinding, "/custom-encoder/echo.svc");
                serviceBuilder.AddServiceEndpoint<PaymentService, IPaymentService>(customEncoderBinding, "/custom-encoder/payments.svc");

                serviceBuilder.ConfigureAllServiceHostBase(host =>
                {
                    if (!serviceHosts.Contains(host))
                    {
                        serviceHosts.Add(host);
                    }

                    foreach (ServiceEndpoint endpoint in host.Description.Endpoints)
                    {
                        string path = endpoint.Address.Uri.AbsolutePath;
                        if (path.StartsWith("/custom-formatter/", StringComparison.Ordinal)
                            || path.StartsWith("/custom-encoder/", StringComparison.Ordinal))
                        {
                            endpoint.EndpointBehaviors.Add(new CustomFormatterEndpointBehavior(formatterMetrics));
                        }
                    }
                });
            });

            await app.StartAsync();

            try
            {
                IServer server = app.Services.GetRequiredService<IServer>();
                IServerAddressesFeature? addressesFeature = server.Features.Get<IServerAddressesFeature>();
                string baseAddress = addressesFeature?.Addresses.FirstOrDefault()
                    ?? throw new InvalidOperationException("Kestrel failed to bind an address.");

                Console.WriteLine($"Listening: {baseAddress}");
                Console.WriteLine($"Runtime: {Environment.Version} | IsDynamicCodeSupported={System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported}");
                PrintDispatchDiagnostics(serviceHosts);

                using HttpClient client = new HttpClient
                {
                    BaseAddress = new Uri(baseAddress, UriKind.Absolute),
                };

                await VerifyEchoAsync(client, "/baseline/echo.svc", "Baseline Echo");
                await VerifyTransferAsync(client, "/baseline/payments.svc", "Baseline Transfer");

                await VerifyEchoAsync(client, "/custom-formatter/echo.svc", "CustomFormatter Echo");
                await VerifyTransferAsync(client, "/custom-formatter/payments.svc", "CustomFormatter Transfer");

                await VerifyEchoAsync(client, "/custom-encoder/echo.svc", "CustomEncoder+Formatter Echo");
                await VerifyTransferAsync(client, "/custom-encoder/payments.svc", "CustomEncoder+Formatter Transfer");

                await VerifyActionMismatchAsync(client, "/baseline/echo.svc");

                Console.WriteLine();
                Console.WriteLine("=== Seam Observations ===");
                Console.WriteLine($"Formatter DeserializeCalls={formatterMetrics.DeserializeCalls}, SerializeCalls={formatterMetrics.SerializeCalls}");
                Console.WriteLine($"Formatter LastReaderType={formatterMetrics.LastReaderType}");
                Console.WriteLine($"Formatter LastWriterType={formatterMetrics.LastWriterType}");
                Console.WriteLine($"Encoder LastInnerEncoderType={encoderMetrics.LastInnerEncoderType}");
                Console.WriteLine($"Encoder ReadArraySegmentCalls={encoderMetrics.ReadArraySegmentCalls}, ReadStreamCalls={encoderMetrics.ReadStreamCalls}");
                Console.WriteLine($"Encoder WriteBufferedCalls={encoderMetrics.WriteBufferedCalls}, WriteStreamCalls={encoderMetrics.WriteStreamCalls}");
                Console.WriteLine($"Encoder BufferManagerTakeCount={encoderMetrics.BufferManagerTakeCount}, LastReadBytes={encoderMetrics.LastBufferedReadBytes}, LastWriteBytes={encoderMetrics.LastBufferedWriteBytes}, LastRentalCapacity={encoderMetrics.LastBufferedRentalCapacity}");

                return 0;
            }
            finally
            {
                await app.StopAsync();
            }
        }

        #endregion

        #region Private Methods

        private static async Task<(HttpStatusCode StatusCode, string ContentType, string Body)> PostSoap11Async(
            HttpClient client,
            string path,
            string soapAction,
            string xml)
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, path);
            request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{soapAction}\"");
            request.Content = new StringContent(xml, Encoding.UTF8, "text/xml");

            using HttpResponseMessage response = await client.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();
            string contentType = response.Content.Headers.ContentType?.ToString() ?? string.Empty;

            return (response.StatusCode, contentType, body);
        }

        private static void PrintDispatchDiagnostics(List<ServiceHostBase> serviceHosts)
        {
            Console.WriteLine();
            Console.WriteLine("=== Dispatch & Invoker Diagnostics ===");

            foreach (ServiceHostBase host in serviceHosts)
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
                            string invokerType = op.Invoker?.GetType().FullName ?? "<null>";
                            string formatterType = op.Formatter?.GetType().FullName ?? "<null>";
                            Console.WriteLine($"{path} | Op={op.Name} | Action={op.Action} | Invoker={invokerType} | Formatter={formatterType}");
                        }
                    }
                }
            }
        }

        private static async Task VerifyActionMismatchAsync(HttpClient client, string path)
        {
            (HttpStatusCode status, string contentType, string body) = await PostSoap11Async(
                client,
                path,
                "urn:echo/IEchoService/UnknownAction",
                EchoRequestEnvelope);

            Console.WriteLine();
            Console.WriteLine($"[Negative ActionMismatch] HTTP {(int)status} {status} | Content-Type={contentType}");
            Console.WriteLine(body);

            if (status != HttpStatusCode.InternalServerError || !body.Contains("ActionNotSupported", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unexpected action mismatch response: {(int)status} {body}");
            }
        }

        private static async Task VerifyEchoAsync(HttpClient client, string path, string label)
        {
            (HttpStatusCode status, string contentType, string body) = await PostSoap11Async(
                client,
                path,
                "urn:echo/IEchoService/Echo",
                EchoRequestEnvelope);

            Console.WriteLine();
            Console.WriteLine($"[{label}] HTTP {(int)status} {status} | Content-Type={contentType}");
            Console.WriteLine(body);

            if (status != HttpStatusCode.OK || !body.Contains("hello-fastsoap", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{label} failed: {(int)status} {body}");
            }
        }

        private static async Task VerifyTransferAsync(HttpClient client, string path, string label)
        {
            (HttpStatusCode status, string contentType, string body) = await PostSoap11Async(
                client,
                path,
                "urn:payments/IPaymentService/Transfer",
                TransferRequestEnvelope);

            Console.WriteLine();
            Console.WriteLine($"[{label}] HTTP {(int)status} {status} | Content-Type={contentType}");
            Console.WriteLine(body);

            if (status != HttpStatusCode.OK
                || !body.Contains("TX-123456", StringComparison.Ordinal)
                || !body.Contains("<Successful>true</Successful>", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{label} failed: {(int)status} {body}");
            }
        }

        #endregion
    }
}
