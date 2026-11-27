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
using FastSoap.Sample.Payments.Contracts;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FastSoap.Sample.Payments
{

    /// <summary>
    /// Sample payment service implementation using deterministic responses.
    /// </summary>
    public sealed class PaymentService : IPaymentService
    {

        #region Fields

        private static readonly DateTime FixedSettlementUtc = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);

        #endregion

        #region Public Methods

        /// <inheritdoc />
        public Task<TransferResult> TransferAsync(TransferRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            bool ok = request.Amount > 0m;
            TransferResult result = new TransferResult
            {
                TransactionId = $"TX-{request.AccountNumber}",
                Successful = ok,
                ProcessedAtUtc = FixedSettlementUtc,
                Status = ok ? PaymentStatus.Completed : PaymentStatus.Rejected
            };

            return Task.FromResult(result);
        }

        #endregion

    }

    /// <summary>
    /// Entry point demonstrating both source-generated and manual FastSoap endpoint registration.
    /// </summary>
    public static class Program
    {

        #region Public Methods

        /// <summary>
        /// Builds and runs the FastSoap sample payments host.
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
            builder.Services.AddScoped<IPaymentService, PaymentService>();

            WebApplication app = builder.Build();

            app.MapGet("/health", static (HttpContext context) =>
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.Headers.ContentType = "text/plain; charset=utf-8";

                return context.Response.WriteAsync("ok", context.RequestAborted);
            });

            Uri publicBaseUri = ResolvePublicBaseUri(builder.Configuration);
            SoapServiceMetadata metadata = CreateSampleMetadata();
            SoapMetadataAddressPolicy generatedAddressPolicy = CreateAddressPolicy(publicBaseUri, "/PaymentService.svc");
            SoapMetadataAddressPolicy manualAddressPolicy = CreateAddressPolicy(publicBaseUri, "/manual/PaymentService.svc");

            // 1. Source-generated endpoint registration (emits /PaymentService.svc via FastSoap.Generators)
            app.MapPaymentService("/PaymentService.svc", soap =>
            {
                soap.ConfigureOptions(static options =>
                {
                    options.EnabledVersions = SoapVersionFlags.Both;
                    options.EnableMetadata = true;
                });

                soap.MapGeneratedMetadata(metadata, generatedAddressPolicy, SoapWsdlGenerationMode.ExternalSchemas);
            });

            // 2. Equivalent manual registration with explicit metadata and strict XmlSerializerOptions
            XmlSerializerOptions strictOptions = new XmlSerializerOptions
            {
                AllowDuplicateProperties = false,
                OmitXmlDeclaration = true,
                UnmappedMemberHandling = XmlUnmappedMemberHandling.Disallow
            };
            PaymentXmlContext manualContext = new PaymentXmlContext(strictOptions);

            SoapOperationDescriptor echoDescriptor = new SoapOperationDescriptor(
                "Echo",
                "urn:echo/IEchoService/Echo",
                "urn:echo/IEchoService/EchoResponse",
                new SoapQName("urn:echo", "Echo"),
                new SoapQName("urn:echo", "EchoResponse"));

            app.MapSoap("/manual/PaymentService.svc", soap =>
            {
                soap.ConfigureOptions(static options =>
                {
                    options.EnabledVersions = SoapVersionFlags.Both;
                    options.EnableMetadata = true;
                });

                soap.MapGeneratedMetadata(metadata, manualAddressPolicy, SoapWsdlGenerationMode.ExternalSchemas);

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
                    descriptor: FastSoap.Sample.Payments.Contracts.PaymentServiceSoapContract.Transfer,
                    requestType: manualContext.TransferRequestWire,
                    responseType: manualContext.TransferResponseWire,
                    handler: static async (wire, context, ct) =>
                    {
                        IPaymentService service = context.Services.GetRequiredService<IPaymentService>();
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
            SoapSchemaType transferMetadataType = SoapSchemaType.Complex(
                new SoapQName("urn:payments", "TransferMetadata"),
                [
                    SoapSchemaElement.CreateMember("CorrelationId", SoapSchemaType.String),
                    SoapSchemaElement.CreateMember("Reference", SoapSchemaType.String, minOccurs: 0, isNillable: true)
                ]);

            SoapSchemaType statusEnumType = SoapSchemaType.Enum(
                new SoapQName("urn:payments", "PaymentStatus"),
                ["Pending", "Completed", "Rejected"]);

            SoapSchemaType transferRequestType = SoapSchemaType.Complex(
                new SoapQName("urn:payments", "TransferRequest"),
                [
                    SoapSchemaElement.CreateMember("AccountNumber", SoapSchemaType.String),
                    SoapSchemaElement.CreateMember("Amount", SoapSchemaType.Decimal),
                    SoapSchemaElement.CreateMember("Currency", SoapSchemaType.String),
                    SoapSchemaElement.CreateMember("Metadata", transferMetadataType, minOccurs: 0, isNillable: true),
                    SoapSchemaElement.CreateMember("Note", SoapSchemaType.String, minOccurs: 0, isNillable: true)
                ]);

            SoapSchemaType transferResultType = SoapSchemaType.Complex(
                new SoapQName("urn:payments", "TransferResult"),
                [
                    SoapSchemaElement.CreateMember("TransactionId", SoapSchemaType.String),
                    SoapSchemaElement.CreateMember("Successful", SoapSchemaType.Boolean),
                    SoapSchemaElement.CreateMember("ProcessedAtUtc", SoapSchemaType.DateTime),
                    SoapSchemaElement.CreateMember("Status", statusEnumType)
                ]);

            SoapOperationMetadata transferOp = new SoapOperationMetadata(
                FastSoap.Sample.Payments.Contracts.PaymentServiceSoapContract.Transfer,
                SoapSchemaElement.CreateWrapped(
                    new SoapQName("urn:payments", "Transfer"),
                    [SoapSchemaElement.CreateMember("request", transferRequestType, minOccurs: 0, isNillable: true)]),
                SoapSchemaElement.CreateWrapped(
                    new SoapQName("urn:payments", "TransferResponse"),
                    [SoapSchemaElement.CreateMember("TransferResult", transferResultType, minOccurs: 0, isNillable: true)]));

            return new SoapServiceMetadata(
                serviceName: "PaymentService",
                targetNamespace: "urn:payments",
                operations: [transferOp],
                supportedVersions: SoapVersionFlags.Both,
                portTypeName: "IPaymentService");
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
