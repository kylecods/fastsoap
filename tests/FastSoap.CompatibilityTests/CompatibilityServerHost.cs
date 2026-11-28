using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using CloudNimble.Text.Xml;
using CloudNimble.Text.Xml.Serialization;

using FastSoap.Abstractions;
using FastSoap.AspNetCore;
using FastSoap.Core.Serialization;
using FastSoap.Metadata;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FastSoap.CompatibilityTests
{

    internal sealed class CompatibilityServerHost : IAsyncDisposable
    {

        #region Fields

        private int _serviceInvocations;

        #endregion

        #region Properties

        public Uri BaseUri { get; }

        public HttpClient HttpClient { get; }

        public RecordingHeaderHandler ApiKeyHeaderHandler { get; }

        public int ServiceInvocations => Volatile.Read(ref _serviceInvocations);

        private WebApplication App { get; }

        #endregion

        #region Constructors

        private CompatibilityServerHost(
            WebApplication app,
            Uri baseUri,
            HttpClient httpClient,
            RecordingHeaderHandler apiKeyHeaderHandler)
        {
            App = app;
            BaseUri = baseUri;
            HttpClient = httpClient;
            ApiKeyHeaderHandler = apiKeyHeaderHandler;
        }

        #endregion

        #region Public Methods

        public async ValueTask DisposeAsync()
        {
            HttpClient.Dispose();
            await App.StopAsync().ConfigureAwait(false);
            await App.DisposeAsync().ConfigureAwait(false);
        }

        public void ResetInvocationCount()
        {
            Interlocked.Exchange(ref _serviceInvocations, 0);
        }

        public static async Task<CompatibilityServerHost> StartAsync()
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();

            builder.Services.AddFastSoap();

            WebApplication app = builder.Build();

            XmlSerializerOptions strictOptions = new XmlSerializerOptions
            {
                AllowDuplicateProperties = false,
                OmitXmlDeclaration = true,
                UnmappedMemberHandling = XmlUnmappedMemberHandling.Disallow
            };
            CompatibilityXmlContext xmlContext = new CompatibilityXmlContext(strictOptions);
            RecordingHeaderHandler apiKeyHandler = new RecordingHeaderHandler(new SoapQName("http://tempuri.org/", "ApiKey"));

            CompatibilityServerHost? holder = null;

            void IncrementInvocation()
            {
                if (holder is not null)
                {
                    Interlocked.Increment(ref holder._serviceInvocations);
                }
            }

            void MapEchoEndpoint(string path, SoapVersionFlags versions)
            {
                app.MapSoap(path, soap =>
                {
                    soap.ConfigureOptions(options =>
                    {
                        options.EnabledVersions = versions;
                        options.MaxRequestBytes = 65536;
                    });

                    soap.MapOperation(
                        descriptor: CompatibilityDescriptors.EchoString,
                        requestType: xmlContext.EchoStringRequestWire,
                        responseType: xmlContext.EchoStringResponseWire,
                        handler: (wire, _, ct) =>
                        {
                            ct.ThrowIfCancellationRequested();
                            IncrementInvocation();

                            if (string.Equals(wire.Echo, "__THROW_INTERNAL__", StringComparison.Ordinal))
                            {
                                throw new InvalidOperationException("SECRET_DB_CONNECTION_STRING");
                            }

                            return ValueTask.FromResult(new EchoStringResponseWire
                            {
                                EchoStringResult = wire.Echo
                            });
                        });
                });
            }

            void MapPaymentEndpoint(string path, SoapVersionFlags versions)
            {
                app.MapSoap(path, soap =>
                {
                    soap.ConfigureOptions(options =>
                    {
                        options.EnabledVersions = versions;
                        options.MaxRequestBytes = 65536;
                    });

                    soap.MapOperation(
                        descriptor: CompatibilityDescriptors.Transfer,
                        requestType: xmlContext.TransferRequestWire,
                        responseType: xmlContext.TransferResponseWire,
                        handler: (wire, callContext, ct) =>
                        {
                            ct.ThrowIfCancellationRequested();
                            IncrementInvocation();

                            if (string.Equals(wire.Request.AccountNumber, "FAULT-RECEIVER", StringComparison.Ordinal) ||
                                string.Equals(wire.Request.AccountNumber, "FAIL-INTERNAL", StringComparison.Ordinal))
                            {
                                throw new InvalidOperationException("SECRET_PAYMENT_SWITCH_PASSWORD");
                            }

                            if (wire.Request.Amount < 0m)
                            {
                                throw new SoapProtocolException(
                                    SoapFaultKind.Sender,
                                    HttpStatusCode.InternalServerError,
                                    "Payment validation failed.",
                                    callContext.Version);
                            }

                            return ValueTask.FromResult(new TransferResponseWire
                            {
                                TransferResult = new TransferResult
                                {
                                    TransactionId = "TX-001",
                                    Successful = true,
                                    ProcessedAtUtc = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc),
                                    Status = PaymentStatus.Completed
                                }
                            });
                        });

                    soap.MapOperation(
                        descriptor: CompatibilityDescriptors.BatchTransfer,
                        requestType: xmlContext.BatchTransferRequestWire,
                        responseType: xmlContext.BatchTransferResponseWire,
                        handler: (wire, _, ct) =>
                        {
                            ct.ThrowIfCancellationRequested();
                            IncrementInvocation();

                            List<TransferResult> results = new List<TransferResult>(wire.Request.Items.Count);
                            for (int index = 0; index < wire.Request.Items.Count; index++)
                            {
                                results.Add(new TransferResult
                                {
                                    TransactionId = $"TX-{wire.Request.BatchId}-{(index + 1):D3}",
                                    Successful = true,
                                    ProcessedAtUtc = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc),
                                    Status = PaymentStatus.Completed
                                });
                            }

                            return ValueTask.FromResult(new BatchTransferResponseWire
                            {
                                BatchTransferResult = new BatchTransferResult
                                {
                                    BatchId = wire.Request.BatchId,
                                    Results = results
                                }
                            });
                        });
                });
            }

            MapEchoEndpoint("/soap11/echo-dcs.svc", SoapVersionFlags.Soap11);
            MapEchoEndpoint("/soap11/echo-xmlser.svc", SoapVersionFlags.Soap11);
            MapEchoEndpoint("/soap12/echo-dcs.svc", SoapVersionFlags.Soap12);
            MapEchoEndpoint("/soap12/echo-xmlser.svc", SoapVersionFlags.Soap12);

            app.MapSoap("/soap11/echo-dcs-header-quota.svc", soap =>
            {
                soap.ConfigureOptions(static options =>
                {
                    options.EnabledVersions = SoapVersionFlags.Soap11;
                    options.MaxRequestBytes = 131072;
                    options.MaxHeaderBytes = 65536;
                });

                soap.MapOperation(
                    descriptor: CompatibilityDescriptors.EchoString,
                    requestType: xmlContext.EchoStringRequestWire,
                    responseType: xmlContext.EchoStringResponseWire,
                    handler: (wire, _, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        IncrementInvocation();

                        return ValueTask.FromResult(new EchoStringResponseWire
                        {
                            EchoStringResult = wire.Echo
                        });
                    });
            });

            MapPaymentEndpoint("/soap11/payment-dcs-urn.svc", SoapVersionFlags.Soap11);
            MapPaymentEndpoint("/soap11/payment-xmlser.svc", SoapVersionFlags.Soap11);
            MapPaymentEndpoint("/soap12/payment-dcs-urn.svc", SoapVersionFlags.Soap12);
            MapPaymentEndpoint("/soap12/payment-xmlser.svc", SoapVersionFlags.Soap12);

            void MapTypedFaultPaymentEndpoint(string path, SoapVersionFlags versions)
            {
                app.MapSoap(path, soap =>
                {
                    soap.ConfigureOptions(options =>
                    {
                        options.EnabledVersions = versions;
                        options.MaxRequestBytes = 65536;
                    });

                    soap.MapOperation(
                        descriptor: CompatibilityDescriptors.Transfer,
                        requestType: xmlContext.TransferRequestWire,
                        responseType: xmlContext.TransferResponseWire,
                        handler: (wire, _, ct) =>
                        {
                            ct.ThrowIfCancellationRequested();
                            IncrementInvocation();

                            if (wire.Request.Amount < 0m)
                            {
                                throw SoapFaultDetailSerializer.CreateFaultException(
                                    SoapFaultKind.Sender,
                                    "Payment validation failed.",
                                    new PaymentFaultDetail
                                    {
                                        ErrorCode = "INVALID_AMOUNT",
                                        Message = "Transfer amount must be non-negative."
                                    },
                                    xmlContext.PaymentFaultDetail,
                                    httpStatusCode: HttpStatusCode.InternalServerError);
                            }

                            if (string.Equals(wire.Request.AccountNumber, "TYPED-RECEIVER", StringComparison.Ordinal))
                            {
                                throw SoapFaultDetailSerializer.CreateFaultException(
                                    SoapFaultKind.Receiver,
                                    "Upstream switch unavailable.",
                                    new PaymentFaultDetail
                                    {
                                        ErrorCode = "SWITCH_OFFLINE",
                                        Message = "Payment switch returned transient failure."
                                    },
                                    xmlContext.PaymentFaultDetail);
                            }

                            return ValueTask.FromResult(new TransferResponseWire
                            {
                                TransferResult = new TransferResult
                                {
                                    TransactionId = "TX-001",
                                    Successful = true,
                                    ProcessedAtUtc = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc),
                                    Status = PaymentStatus.Completed
                                }
                            });
                        });
                });
            }

            MapTypedFaultPaymentEndpoint("/soap11/payment-typed-fault.svc", SoapVersionFlags.Soap11);
            MapTypedFaultPaymentEndpoint("/soap12/payment-typed-fault.svc", SoapVersionFlags.Soap12);

            app.MapSoap("/soap11/echo-headers.svc", soap =>
            {
                soap.ConfigureOptions(static options => options.EnabledVersions = SoapVersionFlags.Soap11);
                soap.AddHeaderHandler(apiKeyHandler);
                soap.MapOperation(
                    descriptor: CompatibilityDescriptors.EchoHeader,
                    requestType: xmlContext.EchoHeaderRequestWire,
                    responseType: xmlContext.EchoHeaderResponseWire,
                    handler: (wire, _, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        IncrementInvocation();

                        return ValueTask.FromResult(new EchoHeaderResponseWire
                        {
                            SayHello = $"Saying Hello {wire.Text}"
                        });
                    });
            });

            string wsdlDir = Path.Combine(GetFixturesRoot(), "wsdl");
            SoapStaticMetadataProvider echoWsdlProvider = SoapStaticMetadataProvider.Create(
                File.ReadAllBytes(Path.Combine(wsdlDir, "echo-soap11.wsdl")),
                [
                    new KeyValuePair<string, byte[]>("xsd0", File.ReadAllBytes(Path.Combine(wsdlDir, "echo-soap11.xsd0.xsd"))),
                    new KeyValuePair<string, byte[]>("xsd1", File.ReadAllBytes(Path.Combine(wsdlDir, "echo-soap11.xsd1.xsd")))
                ]);

            app.MapSoap("/wsdl/echo-soap11.svc", soap =>
            {
                soap.ConfigureOptions(static options =>
                {
                    options.EnabledVersions = SoapVersionFlags.Soap11;
                    options.EnableMetadata = true;
                });
                soap.MapStaticMetadata(echoWsdlProvider);
                soap.MapOperation(
                    descriptor: CompatibilityDescriptors.EchoString,
                    requestType: xmlContext.EchoStringRequestWire,
                    responseType: xmlContext.EchoStringResponseWire,
                    handler: (wire, _, _) => ValueTask.FromResult(new EchoStringResponseWire { EchoStringResult = wire.Echo }));
            });

            SoapStaticMetadataProvider paymentDcsSoap11WsdlProvider = SoapStaticMetadataProvider.Create(
                File.ReadAllBytes(Path.Combine(wsdlDir, "payment-dcs-soap11.single.wsdl")),
                [
                    new KeyValuePair<string, byte[]>("xsd0", File.ReadAllBytes(Path.Combine(wsdlDir, "payment-dcs-soap11.xsd0.xsd"))),
                    new KeyValuePair<string, byte[]>("xsd1", File.ReadAllBytes(Path.Combine(wsdlDir, "payment-dcs-soap11.xsd1.xsd")))
                ]);

            app.MapSoap("/wsdl/payment-dcs-soap11.svc", soap =>
            {
                soap.ConfigureOptions(static options =>
                {
                    options.EnabledVersions = SoapVersionFlags.Soap11;
                    options.EnableMetadata = true;
                });
                soap.MapStaticMetadata(paymentDcsSoap11WsdlProvider);
                soap.MapOperation(
                    descriptor: CompatibilityDescriptors.Transfer,
                    requestType: xmlContext.TransferRequestWire,
                    responseType: xmlContext.TransferResponseWire,
                    handler: (_, _, _) => ValueTask.FromResult(new TransferResponseWire
                    {
                        TransferResult = new TransferResult
                        {
                            TransactionId = "TX-001",
                            Successful = true,
                            ProcessedAtUtc = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc),
                            Status = PaymentStatus.Completed
                        }
                    }));
            });

            SoapStaticMetadataProvider paymentXmlSerSoap11WsdlProvider = SoapStaticMetadataProvider.Create(
                File.ReadAllBytes(Path.Combine(wsdlDir, "payment-xmlser-soap11.single.wsdl")));

            app.MapSoap("/wsdl/payment-xmlser-soap11.svc", soap =>
            {
                soap.ConfigureOptions(static options =>
                {
                    options.EnabledVersions = SoapVersionFlags.Soap11;
                    options.EnableMetadata = true;
                });
                soap.MapStaticMetadata(paymentXmlSerSoap11WsdlProvider);
                soap.MapOperation(
                    descriptor: CompatibilityDescriptors.Transfer,
                    requestType: xmlContext.TransferRequestWire,
                    responseType: xmlContext.TransferResponseWire,
                    handler: (_, _, _) => ValueTask.FromResult(new TransferResponseWire
                    {
                        TransferResult = new TransferResult
                        {
                            TransactionId = "TX-001",
                            Successful = true,
                            ProcessedAtUtc = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc),
                            Status = PaymentStatus.Completed
                        }
                    }));
            });

            SoapStaticMetadataProvider paymentDcsSoap12WsdlProvider = SoapStaticMetadataProvider.Create(
                File.ReadAllBytes(Path.Combine(wsdlDir, "payment-dcs-soap12.single.wsdl")));

            app.MapSoap("/wsdl/payment-dcs-soap12.svc", soap =>
            {
                soap.ConfigureOptions(static options =>
                {
                    options.EnabledVersions = SoapVersionFlags.Soap12;
                    options.EnableMetadata = true;
                });
                soap.MapStaticMetadata(paymentDcsSoap12WsdlProvider);
                soap.MapOperation(
                    descriptor: CompatibilityDescriptors.Transfer,
                    requestType: xmlContext.TransferRequestWire,
                    responseType: xmlContext.TransferResponseWire,
                    handler: (_, _, _) => ValueTask.FromResult(new TransferResponseWire
                    {
                        TransferResult = new TransferResult
                        {
                            TransactionId = "TX-001",
                            Successful = true,
                            ProcessedAtUtc = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc),
                            Status = PaymentStatus.Completed
                        }
                    }));
            });

            await app.StartAsync().ConfigureAwait(false);

            IServer server = app.Services.GetRequiredService<IServer>();
            IServerAddressesFeature addresses = server.Features.Get<IServerAddressesFeature>()!;
            Uri baseUri = new Uri(addresses.Addresses.First());
            HttpClient httpClient = new HttpClient { BaseAddress = baseUri };

            holder = new CompatibilityServerHost(app, baseUri, httpClient, apiKeyHandler);

            return holder;
        }

        public static string GetFixturesRoot()
        {
            DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current is not null)
            {
                string candidate = Path.Combine(current.FullName, "tests", "fixtures");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate tests/fixtures directory.");
        }

        #endregion

    }

}
