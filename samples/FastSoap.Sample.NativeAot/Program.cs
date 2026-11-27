using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using CloudNimble.Text.Xml;
using CloudNimble.Text.Xml.Serialization;

using FastSoap.Abstractions;
using FastSoap.AspNetCore;
using FastSoap.Metadata;
using FastSoap.Sample.NativeAot.Contracts;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FastSoap.Sample.NativeAot
{

    /// <summary>
    /// Native AOT payment service implementation.
    /// </summary>
    public sealed class NativePaymentService : INativePaymentService
    {

        #region Fields

        private static long s_sequence;

        #endregion

        #region Public Methods

        /// <inheritdoc />
        public Task<TransferResult> TransferAsync(TransferRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            long seq = Interlocked.Increment(ref s_sequence);

            return Task.FromResult(new TransferResult
            {
                TransactionId = $"TX-{seq:D3}",
                Successful = request.Amount > 0m
            });
        }

        #endregion

    }

    /// <summary>
    /// Native AOT Kestrel host entry point for FastSoap.
    /// </summary>
    public static class Program
    {

        #region Public Methods

        /// <summary>
        /// Builds and runs the Native AOT FastSoap sample service.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>A task representing host shutdown.</returns>
        public static async Task Main(string[] args)
        {
            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args);
            builder.Services.AddFastSoap(static options =>
            {
                options.EnabledVersions = SoapVersionFlags.Both;
            });
            builder.Services.AddScoped<INativePaymentService, NativePaymentService>();

            WebApplication app = builder.Build();

            app.MapGet("/health", static (HttpContext context) =>
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.Headers.ContentType = "text/plain; charset=utf-8";

                return context.Response.WriteAsync("ok", context.RequestAborted);
            });

            Uri publicBaseUri = ResolvePublicBaseUri(builder.Configuration);
            SoapServiceMetadata metadata = CreateSampleMetadata();
            SoapMetadataAddressPolicy nativeAddressPolicy = CreateAddressPolicy(publicBaseUri, "/NativePaymentService.svc");
            SoapMetadataAddressPolicy serviceAddressPolicy = CreateAddressPolicy(publicBaseUri, "/Service.svc");

            app.MapNativePaymentService("/NativePaymentService.svc", soap =>
            {
                soap.ConfigureOptions(static options =>
                {
                    options.EnabledVersions = SoapVersionFlags.Both;
                    options.EnableMetadata = true;
                });

                soap.MapGeneratedMetadata(metadata, nativeAddressPolicy, SoapWsdlGenerationMode.InlineSchemas);
            });

            XmlSerializerOptions strictOptions = new XmlSerializerOptions
            {
                AllowDuplicateProperties = false,
                OmitXmlDeclaration = true,
                UnmappedMemberHandling = XmlUnmappedMemberHandling.Disallow
            };
            NativeAotXmlContext manualContext = new NativeAotXmlContext(strictOptions);

            SoapOperationDescriptor echoDescriptor = new SoapOperationDescriptor(
                "Echo",
                "urn:echo/IEchoService/Echo",
                "urn:echo/IEchoService/EchoResponse",
                new SoapQName("urn:echo", "Echo"),
                new SoapQName("urn:echo", "EchoResponse"));

            app.MapSoap("/Service.svc", soap =>
            {
                soap.ConfigureOptions(static options =>
                {
                    options.EnabledVersions = SoapVersionFlags.Both;
                    options.EnableMetadata = true;
                });

                soap.MapGeneratedMetadata(metadata, serviceAddressPolicy, SoapWsdlGenerationMode.InlineSchemas);

                soap.MapOperation(
                    descriptor: echoDescriptor,
                    requestType: manualContext.EchoRequest,
                    responseType: manualContext.EchoResponse,
                    handler: static (request, _, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();

                        return ValueTask.FromResult(new EchoResponse { EchoResult = request.Message });
                    });

                soap.MapOperation(
                    descriptor: NativePaymentServiceSoapContract.Transfer,
                    requestType: manualContext.TransferRequestWire,
                    responseType: manualContext.TransferResponseWire,
                    handler: static async (wire, context, ct) =>
                    {
                        INativePaymentService service = context.Services.GetRequiredService<INativePaymentService>();
                        TransferResult result = await service.TransferAsync(wire.Request, ct).ConfigureAwait(false);

                        return new TransferResponseWire { Result = result };
                    });
            });

            await app.RunAsync().ConfigureAwait(false);
        }

        #endregion

        #region Private Methods

        private static SoapMetadataAddressPolicy CreateAddressPolicy(Uri publicBaseUri, string endpointPath)
        {
            string baseAddress = publicBaseUri
                .GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped)
                .TrimEnd('/');
            string normalizedPath = endpointPath.StartsWith('/') ? endpointPath : $"/{endpointPath}";

            return new SoapMetadataAddressPolicy($"{baseAddress}{normalizedPath}");
        }

        private static SoapServiceMetadata CreateSampleMetadata()
        {
            return new SoapServiceMetadata(
                serviceName: "NativePaymentService",
                targetNamespace: "urn:payments",
                operations:
                [
                    new SoapOperationMetadata(
                        NativePaymentServiceSoapContract.Transfer,
                        SoapSchemaElement.CreateWrapped(
                            new SoapQName("urn:payments", "Transfer"),
                            [SoapSchemaElement.CreateMember("request", SoapSchemaType.Complex(
                                new SoapQName("urn:payments", "TransferRequest"),
                                [
                                    SoapSchemaElement.CreateMember("AccountNumber", SoapSchemaType.String),
                                    SoapSchemaElement.CreateMember("Amount", SoapSchemaType.Decimal)
                                ]), minOccurs: 0, isNillable: true)]),
                        SoapSchemaElement.CreateWrapped(
                            new SoapQName("urn:payments", "TransferResponse"),
                            [SoapSchemaElement.CreateMember("TransferResult", SoapSchemaType.Complex(
                                new SoapQName("urn:payments", "TransferResult"),
                                [
                                    SoapSchemaElement.CreateMember("TransactionId", SoapSchemaType.String),
                                    SoapSchemaElement.CreateMember("Successful", SoapSchemaType.Boolean)
                                ]), minOccurs: 0, isNillable: true)]))
                ],
                supportedVersions: SoapVersionFlags.Both,
                portTypeName: "INativePaymentService");
        }

        private static Uri ResolvePublicBaseUri(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            string? configuredBaseUrl = configuration["FastSoap:PublicBaseUrl"];
            if (!string.IsNullOrWhiteSpace(configuredBaseUrl))
            {
                if (!TryCreatePublicBaseUri(configuredBaseUrl, out Uri? explicitBaseUri))
                {
                    throw new InvalidOperationException(
                        "Configuration value 'FastSoap:PublicBaseUrl' must be an absolute non-wildcard HTTP or HTTPS URI without userinfo, query, or fragment.");
                }

                return explicitBaseUri;
            }

            string? configuredUrls = configuration["urls"];
            if (!string.IsNullOrWhiteSpace(configuredUrls))
            {
                string[] candidates = configuredUrls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                for (int index = 0; index < candidates.Length; index++)
                {
                    if (TryCreatePublicBaseUri(candidates[index], out Uri? urlCandidate))
                    {
                        return urlCandidate;
                    }
                }
            }

            return new Uri("http://localhost:5000", UriKind.Absolute);
        }

        private static bool TryCreatePublicBaseUri(
            string? value,
            [NotNullWhen(true)] out Uri? uri)
        {
            uri = null;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string trimmed = value.Trim();
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? candidate))
            {
                return false;
            }

            if (!string.Equals(candidate.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(candidate.Host)
                || !string.IsNullOrWhiteSpace(candidate.UserInfo)
                || !string.IsNullOrWhiteSpace(candidate.Query)
                || !string.IsNullOrWhiteSpace(candidate.Fragment))
            {
                return false;
            }

            if (string.Equals(candidate.Host, "*", StringComparison.Ordinal)
                || string.Equals(candidate.Host, "+", StringComparison.Ordinal))
            {
                return false;
            }

            if (IPAddress.TryParse(candidate.DnsSafeHost, out IPAddress? ipAddress)
                && (ipAddress.Equals(IPAddress.Any) || ipAddress.Equals(IPAddress.IPv6Any)))
            {
                return false;
            }

            uri = candidate;

            return true;
        }

        #endregion

    }

}
