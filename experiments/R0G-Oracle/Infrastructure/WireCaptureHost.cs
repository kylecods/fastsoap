using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using CoreWCF.Configuration;
using CoreWCF.Description;

using FastSoap.R0G.Oracle.Contracts;
using FastSoap.R0G.Oracle.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FastSoap.R0G.Oracle.Infrastructure
{
    /// <summary>
    /// Represents a verbatim HTTP wire capture recorded from a real WCF/CoreWCF exchange.
    /// </summary>
    public sealed class CapturedWireExchange
    {
        #region Properties

        /// <summary>
        /// Gets the request path that was invoked.
        /// </summary>
        public required string Path { get; init; }

        /// <summary>
        /// Gets the raw UTF-8/binary request body bytes received by CoreWCF.
        /// </summary>
        public required byte[] RequestBody { get; init; }

        /// <summary>
        /// Gets the HTTP <c>Content-Type</c> request header.
        /// </summary>
        public string? RequestContentType { get; init; }

        /// <summary>
        /// Gets the HTTP <c>SOAPAction</c> request header, if present.
        /// </summary>
        public string? RequestSoapAction { get; init; }

        /// <summary>
        /// Gets the raw response body bytes produced by CoreWCF.
        /// </summary>
        public required byte[] ResponseBody { get; init; }

        /// <summary>
        /// Gets the HTTP <c>Content-Type</c> response header, if present.
        /// </summary>
        public string? ResponseContentType { get; init; }

        /// <summary>
        /// Gets the HTTP status code returned by CoreWCF.
        /// </summary>
        public required int ResponseStatusCode { get; init; }

        #endregion
    }

    /// <summary>
    /// Hosts real CoreWCF 1.9.2 HTTP endpoints on Kestrel with verbatim request/response wire capture.
    /// </summary>
    public sealed class WireCaptureHost : IAsyncDisposable
    {
        #region Fields

        private readonly WebApplication _app;
        private readonly ConcurrentQueue<CapturedWireExchange> _captures;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the base HTTP URI of the running Kestrel host.
        /// </summary>
        public Uri BaseUri { get; }

        #endregion

        #region Constructors

        private WireCaptureHost(WebApplication app, Uri baseUri, ConcurrentQueue<CapturedWireExchange> captures)
        {
            ArgumentNullException.ThrowIfNull(app);
            ArgumentNullException.ThrowIfNull(baseUri);
            ArgumentNullException.ThrowIfNull(captures);

            _app = app;
            BaseUri = baseUri;
            _captures = captures;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Creates a WCF client <see cref="Binding"/> matching SOAP 1.1 (<see cref="BasicHttpBinding"/>) or
        /// SOAP 1.2 without WS-Addressing (<see cref="CustomBinding"/>).
        /// </summary>
        /// <param name="soap12"><see langword="true"/> for SOAP 1.2 without WS-Addressing; <see langword="false"/> for SOAP 1.1.</param>
        /// <returns>The configured WCF client binding.</returns>
        public static Binding CreateClientBinding(bool soap12)
        {
            if (!soap12)
            {
                BasicHttpBinding soap11Binding = new BasicHttpBinding(BasicHttpSecurityMode.None)
                {
                    MaxReceivedMessageSize = 1024 * 1024,
                    TransferMode = TransferMode.Buffered
                };

                return soap11Binding;
            }

            CustomBinding soap12Binding = new CustomBinding(
                new TextMessageEncodingBindingElement(MessageVersion.CreateVersion(EnvelopeVersion.Soap12, AddressingVersion.None), Encoding.UTF8),
                new HttpTransportBindingElement
                {
                    AuthenticationScheme = AuthenticationSchemes.Anonymous,
                    MaxReceivedMessageSize = 1024 * 1024,
                    TransferMode = TransferMode.Buffered
                });

            return soap12Binding;
        }

        /// <summary>
        /// Creates a CoreWCF server <see cref="CoreWCF.Channels.Binding"/> matching SOAP 1.1 or SOAP 1.2 without WS-Addressing.
        /// </summary>
        /// <param name="soap12"><see langword="true"/> for SOAP 1.2 without WS-Addressing; <see langword="false"/> for SOAP 1.1.</param>
        /// <param name="maxReceivedMessageSize">Maximum received message size in bytes.</param>
        /// <returns>The configured CoreWCF server binding.</returns>
        public static CoreWCF.Channels.Binding CreateServerBinding(bool soap12, long maxReceivedMessageSize = 65536)
        {
            if (!soap12)
            {
                CoreWCF.BasicHttpBinding soap11Binding = new CoreWCF.BasicHttpBinding(CoreWCF.Channels.BasicHttpSecurityMode.None)
                {
                    MaxReceivedMessageSize = maxReceivedMessageSize,
                    TransferMode = CoreWCF.TransferMode.Buffered
                };

                return soap11Binding;
            }

            CoreWCF.Channels.CustomBinding soap12Binding = new CoreWCF.Channels.CustomBinding(
                new CoreWCF.Channels.TextMessageEncodingBindingElement
                {
                    MessageVersion = CoreWCF.Channels.MessageVersion.Soap12
                },
                new CoreWCF.Channels.HttpTransportBindingElement
                {
                    AuthenticationScheme = AuthenticationSchemes.Anonymous,
                    MaxReceivedMessageSize = maxReceivedMessageSize,
                    TransferMode = CoreWCF.TransferMode.Buffered
                });

            return soap12Binding;
        }

        /// <summary>
        /// Starts a dedicated single-service CoreWCF Kestrel host with <see cref="ServiceMetadataBehavior"/> enabled
        /// for capturing clean WSDL 1.1 and XSD artifacts.
        /// </summary>
        /// <typeparam name="TService">CoreWCF service implementation type.</typeparam>
        /// <typeparam name="TContract">CoreWCF service contract interface type.</typeparam>
        /// <param name="relativePath">Endpoint relative path, e.g. <c>/Service.svc</c>.</param>
        /// <param name="soap12"><see langword="true"/> for SOAP 1.2 without WS-Addressing; <see langword="false"/> for SOAP 1.1.</param>
        /// <returns>A running <see cref="WireCaptureHost"/> exposing WSDL/XSD metadata.</returns>
        public static async Task<WireCaptureHost> StartMetadataHostAsync<TService, TContract>(string relativePath, bool soap12)
            where TService : class
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

            int port = GetAvailableTcpPort();
            Uri baseUri = new Uri($"http://127.0.0.1:{port}", UriKind.Absolute);
            ConcurrentQueue<CapturedWireExchange> queue = new ConcurrentQueue<CapturedWireExchange>();

            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel(options =>
            {
                options.Listen(IPAddress.Loopback, port);
            });

            builder.Services.AddServiceModelServices();
            builder.Services.AddServiceModelMetadata();
            builder.Services.AddTransient<TService>();

            WebApplication app = builder.Build();
            app.UseServiceModel(serviceBuilder =>
            {
                serviceBuilder.AddService<TService>(options =>
                {
                    options.BaseAddresses.Clear();
                    options.BaseAddresses.Add(baseUri);
                });
                serviceBuilder.AddServiceEndpoint<TService, TContract>(CreateServerBinding(soap12), relativePath);
            });

            ServiceMetadataBehavior metadataBehavior = app.Services.GetRequiredService<ServiceMetadataBehavior>();
            metadataBehavior.HttpGetEnabled = true;

            await app.StartAsync().ConfigureAwait(false);

            return new WireCaptureHost(app, baseUri, queue);
        }

        /// <summary>
        /// Starts the multi-endpoint CoreWCF Kestrel oracle host covering SOAP 1.1 and SOAP 1.2 contracts.
        /// </summary>
        /// <returns>A running <see cref="WireCaptureHost"/>.</returns>
        public static async Task<WireCaptureHost> StartOracleAsync()
        {
            int port = GetAvailableTcpPort();
            Uri baseUri = new Uri($"http://127.0.0.1:{port}", UriKind.Absolute);
            ConcurrentQueue<CapturedWireExchange> queue = new ConcurrentQueue<CapturedWireExchange>();

            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel(options =>
            {
                options.Limits.MaxRequestBodySize = 10 * 1024 * 1024;
                options.Listen(IPAddress.Loopback, port);
            });

            builder.Services.AddServiceModelServices();
            builder.Services.AddTransient<EchoDcsService>();
            builder.Services.AddTransient<EchoXmlSerializerService>();
            builder.Services.AddTransient<EchoWithHeadersService>();
            builder.Services.AddTransient<PaymentDcsDefaultService>();
            builder.Services.AddTransient<PaymentDcsUrnService>();
            builder.Services.AddTransient<PaymentXmlSerializerService>();

            WebApplication app = builder.Build();

            app.Use(async (HttpContext context, RequestDelegate next) =>
            {
                if (!HttpMethods.IsPost(context.Request.Method))
                {
                    await next(context).ConfigureAwait(false);
                    return;
                }

                await CaptureExchangeAsync(context, next, queue).ConfigureAwait(false);
            });

            app.UseServiceModel(serviceBuilder =>
            {
                CoreWCF.Channels.Binding soap11 = CreateServerBinding(soap12: false, maxReceivedMessageSize: 65536);
                CoreWCF.Channels.Binding soap12 = CreateServerBinding(soap12: true, maxReceivedMessageSize: 65536);

                serviceBuilder.AddService<EchoDcsService>();
                serviceBuilder.AddServiceEndpoint<EchoDcsService, IEchoDcsService>(soap11, "/soap11/echo-dcs.svc");
                serviceBuilder.AddServiceEndpoint<EchoDcsService, IEchoDcsService>(soap12, "/soap12/echo-dcs.svc");

                serviceBuilder.AddService<EchoXmlSerializerService>();
                serviceBuilder.AddServiceEndpoint<EchoXmlSerializerService, IEchoXmlSerializerService>(soap11, "/soap11/echo-xmlser.svc");
                serviceBuilder.AddServiceEndpoint<EchoXmlSerializerService, IEchoXmlSerializerService>(soap12, "/soap12/echo-xmlser.svc");

                serviceBuilder.AddService<EchoWithHeadersService>();
                serviceBuilder.AddServiceEndpoint<EchoWithHeadersService, IEchoWithHeadersService>(soap11, "/soap11/echo-headers.svc");

                serviceBuilder.AddService<PaymentDcsDefaultService>();
                serviceBuilder.AddServiceEndpoint<PaymentDcsDefaultService, IPaymentDcsDefaultService>(soap11, "/soap11/payment-dcs-default.svc");

                serviceBuilder.AddService<PaymentDcsUrnService>();
                serviceBuilder.AddServiceEndpoint<PaymentDcsUrnService, IPaymentDcsUrnService>(soap11, "/soap11/payment-dcs-urn.svc");
                serviceBuilder.AddServiceEndpoint<PaymentDcsUrnService, IPaymentDcsUrnService>(soap12, "/soap12/payment-dcs-urn.svc");

                serviceBuilder.AddService<PaymentXmlSerializerService>();
                serviceBuilder.AddServiceEndpoint<PaymentXmlSerializerService, IPaymentXmlSerializerService>(soap11, "/soap11/payment-xmlser.svc");
                serviceBuilder.AddServiceEndpoint<PaymentXmlSerializerService, IPaymentXmlSerializerService>(soap12, "/soap12/payment-xmlser.svc");
            });

            await app.StartAsync().ConfigureAwait(false);

            return new WireCaptureHost(app, baseUri, queue);
        }

        /// <summary>
        /// Clears all previously recorded wire exchanges.
        /// </summary>
        public void ClearCaptures()
        {
            while (_captures.TryDequeue(out _))
            {
            }
        }

        /// <summary>
        /// Builds an absolute endpoint <see cref="Uri"/> for the specified relative path.
        /// </summary>
        /// <param name="relativePath">Relative endpoint path.</param>
        /// <returns>The full <see cref="Uri"/>.</returns>
        public Uri CreateEndpointUri(string relativePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

            Uri endpointUri = new Uri(BaseUri, relativePath);

            return endpointUri;
        }

        /// <summary>
        /// Dequeues the single captured wire exchange from the last request.
        /// </summary>
        /// <returns>The captured wire exchange.</returns>
        public CapturedWireExchange DequeueSingleCapture()
        {
            if (!_captures.TryDequeue(out CapturedWireExchange? capture))
            {
                throw new InvalidOperationException("Expected a captured SOAP HTTP exchange, but the capture queue was empty.");
            }

            if (_captures.TryDequeue(out _))
            {
                throw new InvalidOperationException("Expected exactly one captured SOAP HTTP exchange, but multiple exchanges were queued.");
            }

            return capture;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync().ConfigureAwait(false);
            await _app.DisposeAsync().ConfigureAwait(false);
        }

        #endregion

        #region Private Methods

        private static async Task CaptureExchangeAsync(
            HttpContext context,
            RequestDelegate next,
            ConcurrentQueue<CapturedWireExchange> queue)
        {
            MemoryStream requestBuffer = new MemoryStream();
            await context.Request.Body.CopyToAsync(requestBuffer).ConfigureAwait(false);
            byte[] requestBytes = requestBuffer.ToArray();
            requestBuffer.Position = 0;

            Stream originalRequestBody = context.Request.Body;
            context.Request.Body = requestBuffer;

            IHttpResponseBodyFeature? originalBodyFeature = context.Features.Get<IHttpResponseBodyFeature>();
            BufferingResponseBodyFeature bufferingFeature = new BufferingResponseBodyFeature();
            context.Features.Set<IHttpResponseBodyFeature>(bufferingFeature);

            try
            {
                await next(context).ConfigureAwait(false);

                await bufferingFeature.FlushToStreamAsync().ConfigureAwait(false);
                byte[] responseBytes = bufferingFeature.Buffer.ToArray();
                string? soapAction = context.Request.Headers.TryGetValue("SOAPAction", out Microsoft.Extensions.Primitives.StringValues actionValues)
                    ? actionValues.ToString()
                    : null;

                queue.Enqueue(new CapturedWireExchange
                {
                    Path = context.Request.Path.Value ?? string.Empty,
                    RequestBody = requestBytes,
                    RequestContentType = context.Request.ContentType,
                    RequestSoapAction = soapAction,
                    ResponseBody = responseBytes,
                    ResponseContentType = context.Response.ContentType,
                    ResponseStatusCode = context.Response.StatusCode
                });

                if (originalBodyFeature is not null && responseBytes.Length > 0)
                {
                    await originalBodyFeature.Stream.WriteAsync(responseBytes, context.RequestAborted).ConfigureAwait(false);
                    await originalBodyFeature.Stream.FlushAsync(context.RequestAborted).ConfigureAwait(false);
                }
            }
            finally
            {
                if (originalBodyFeature is not null)
                {
                    context.Features.Set(originalBodyFeature);
                }

                context.Request.Body = originalRequestBody;
                await requestBuffer.DisposeAsync().ConfigureAwait(false);
                await bufferingFeature.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static int GetAvailableTcpPort()
        {
            using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            return port;
        }

        #endregion

        #region Nested Types

        private sealed class BufferingResponseBodyFeature : IHttpResponseBodyFeature, IAsyncDisposable
        {
            #region Fields

            private readonly PipeWriter _writer;

            #endregion

            #region Properties

            public MemoryStream Buffer { get; }

            public Stream Stream => Buffer;

            public PipeWriter Writer => _writer;

            #endregion

            #region Constructors

            public BufferingResponseBodyFeature()
            {
                Buffer = new MemoryStream();
                _writer = PipeWriter.Create(Buffer, new StreamPipeWriterOptions(leaveOpen: true));
            }

            #endregion

            #region Public Methods

            public async Task CompleteAsync()
            {
                await _writer.FlushAsync().ConfigureAwait(false);
                await _writer.CompleteAsync().ConfigureAwait(false);
            }

            public void DisableBuffering()
            {
            }

            public async ValueTask DisposeAsync()
            {
                await _writer.CompleteAsync().ConfigureAwait(false);
                await Buffer.DisposeAsync().ConfigureAwait(false);
            }

            public async Task FlushToStreamAsync()
            {
                await _writer.FlushAsync().ConfigureAwait(false);
            }

            public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task StartAsync(CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            #endregion
        }

        #endregion
    }
}
