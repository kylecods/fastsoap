using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using R0B.SoapCoreSpike.Contracts;
using R0B.SoapCoreSpike.Services;
using R0B.SoapCoreSpike.SoapCoreAdaptation;
using SoapCore;
using SoapCore.Extensibility;

namespace R0B.SoapCoreSpike
{
    /// <summary>
    /// Entry point for the R0-B SoapCore adaptation spike hosting baseline and XmlEssentials-adapted SOAP 1.1 endpoints.
    /// </summary>
    public static class Program
    {
        #region Public Methods

        /// <summary>
        /// Configures and runs the SoapCore spike server or executes the self-hosted smoke suite when <c>--smoke-test</c> is passed.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Process exit code (<c>0</c> on success).</returns>
        public static async Task<int> Main(string[] args)
        {
            bool smokeTest = args.Any(static arg => string.Equals(arg, "--smoke-test", StringComparison.OrdinalIgnoreCase));

            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

            if (smokeTest)
            {
                builder.WebHost.UseUrls("http://127.0.0.1:0");
                builder.Logging.SetMinimumLevel(LogLevel.Warning);
            }

            builder.Services.AddSoapCore();
            builder.Services.AddSingleton<IOperationInvoker, TypedPaymentOperationInvoker>();
            builder.Services.AddCustomSoapMessageSerializer<XmlEssentialsSerializationHandler>();
            builder.Services.AddScoped<IPaymentService, PaymentService>();

            WebApplication app = builder.Build();
            IEndpointRouteBuilder endpoints = app;

            endpoints.UseSoapEndpoint<IPaymentService>(options =>
            {
                options.Path = "/PaymentService.svc";
                options.EncoderOptions = [new SoapEncoderOptions()];
                options.SoapSerializer = SoapSerializer.XmlSerializer;
                options.OmitXmlDeclaration = true;
                options.IndentXml = false;
            });

            endpoints.UseSoapEndpoint<IPaymentService>(options =>
            {
                options.Path = "/PaymentServiceXmlEssentials.svc";
                options.EncoderOptions = [new SoapEncoderOptions()];
                options.SoapSerializer = SoapSerializer.XmlSerializer;
                options.OmitXmlDeclaration = true;
                options.IndentXml = false;
                options.UseCustomSerializer<XmlEssentialsSerializationHandler>();
            });

            if (!smokeTest)
            {
                await app.RunAsync().ConfigureAwait(false);

                return 0;
            }

            await app.StartAsync().ConfigureAwait(false);

            try
            {
                IServer server = app.Services.GetRequiredService<IServer>();
                IServerAddressesFeature addresses = server.Features.Get<IServerAddressesFeature>()
                    ?? throw new InvalidOperationException("No server addresses feature available.");
                string baseUrl = addresses.Addresses.First();

                await RunSmokeTestsAsync(baseUrl).ConfigureAwait(false);
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

        private static async Task PostSoapAsync(
            HttpClient client,
            string url,
            string soapAction,
            string bodyXml,
            string label)
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("SOAPAction", $"\"{soapAction}\"");
            request.Content = new StringContent(bodyXml, Encoding.UTF8, "text/xml");
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("text/xml; charset=utf-8");

            using HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);
            string responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            Console.WriteLine($"[{label}] HTTP {(int)response.StatusCode} {response.StatusCode}");
            Console.WriteLine(responseText);
            Console.WriteLine();
        }

        private static async Task RunSmokeTestsAsync(string baseUrl)
        {
            using HttpClient client = new HttpClient();

            const string echoEnvelope =
                """
                <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/">
                  <s:Body>
                    <Echo xmlns="urn:payments">
                      <message>hello-fastsoap</message>
                    </Echo>
                  </s:Body>
                </s:Envelope>
                """;

            const string transferEnvelope =
                """
                <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/">
                  <s:Body>
                    <Transfer xmlns="urn:payments">
                      <request><AccountNumber>123456</AccountNumber><Amount>100.00</Amount></request>
                    </Transfer>
                  </s:Body>
                </s:Envelope>
                """;

            const string invalidEnvelope =
                """
                <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/">
                  <s:Body>
                    <Transfer xmlns="urn:payments">
                      <request><AccountNumber>123456</AccountNumber><Amount>not-a-decimal</Amount></request>
                    </Transfer>
                  </s:Body>
                </s:Envelope>
                """;

            await PostSoapAsync(
                client,
                $"{baseUrl}/PaymentService.svc",
                "urn:payments/IPaymentService/Echo",
                echoEnvelope,
                "Baseline Echo");

            await PostSoapAsync(
                client,
                $"{baseUrl}/PaymentService.svc",
                "urn:payments/IPaymentService/Transfer",
                transferEnvelope,
                "Baseline Transfer");

            await PostSoapAsync(
                client,
                $"{baseUrl}/PaymentServiceXmlEssentials.svc",
                "urn:payments/IPaymentService/Echo",
                echoEnvelope,
                "XmlEssentials-Ingress Echo");

            await PostSoapAsync(
                client,
                $"{baseUrl}/PaymentServiceXmlEssentials.svc",
                "urn:payments/IPaymentService/Transfer",
                transferEnvelope,
                "XmlEssentials-Ingress Transfer");

            await PostSoapAsync(
                client,
                $"{baseUrl}/PaymentServiceXmlEssentials.svc",
                "urn:payments/IPaymentService/UnknownAction",
                transferEnvelope,
                "Unknown Action Fault");

            await PostSoapAsync(
                client,
                $"{baseUrl}/PaymentServiceXmlEssentials.svc",
                "urn:payments/IPaymentService/Transfer",
                invalidEnvelope,
                "Invalid Payload Fault");

            using HttpResponseMessage wsdlResponse = await client.GetAsync($"{baseUrl}/PaymentService.svc?wsdl").ConfigureAwait(false);
            string wsdl = await wsdlResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            Console.WriteLine($"[WSDL] HTTP {(int)wsdlResponse.StatusCode} {wsdlResponse.StatusCode} (length={wsdl.Length})");
            Console.WriteLine(wsdl);
        }

        #endregion
    }
}
