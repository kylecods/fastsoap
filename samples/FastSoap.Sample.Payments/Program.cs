using System;
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

            // 1. Source-generated endpoint registration (emits /PaymentService.svc via FastSoap.Generators)
            app.MapPaymentService("/PaymentService.svc");

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

            SoapServiceMetadata metadata = CreateSampleMetadata();
            SoapMetadataAddressPolicy addressPolicy = new SoapMetadataAddressPolicy("https://payments.example.com/manual/PaymentService.svc");

            app.MapSoap("/manual/PaymentService.svc", soap =>
            {
                soap.ConfigureOptions(static options =>
                {
                    options.EnabledVersions = SoapVersionFlags.Both;
                    options.EnableMetadata = true;
                });

                soap.MapGeneratedMetadata(metadata, addressPolicy, SoapWsdlGenerationMode.ExternalSchemas);

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

        #endregion

    }

}
