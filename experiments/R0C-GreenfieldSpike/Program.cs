using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using CloudNimble.Text.Xml;
using CloudNimble.Text.Xml.Serialization;
using FastSoap.R0C.GreenfieldSpike.Buffers;
using FastSoap.R0C.GreenfieldSpike.Contracts;
using FastSoap.R0C.GreenfieldSpike.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace FastSoap.R0C.GreenfieldSpike
{

    /// <summary>
    /// Isolated R0-C greenfield Native AOT SOAP 1.1 HTTP spike host.
    /// </summary>
    public static class Program
    {

        #region Fields

        private const int MaxFaultBytes = 16 * 1024;
        private const int MaxRequestBytes = 1024 * 1024;
        private const int MaxResponseBytes = 2 * 1024 * 1024;
        private static long s_echoInvocations;
        private static long s_transferInvocations;

        #endregion

        #region Public Methods

        /// <summary>
        /// Entry point for the R0-C greenfield Native AOT spike server.
        /// </summary>
        /// <param name="args">Command-line arguments (supports <c>--urls</c>).</param>
        /// <returns>Process exit code.</returns>
        public static async Task<int> Main(string[] args)
        {
            long startTimestamp = Stopwatch.GetTimestamp();

            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args);
            builder.Logging.ClearProviders();

            XmlSerializerOptions serializerOptions = new XmlSerializerOptions
            {
                AllowDuplicateProperties = false,
                OmitXmlDeclaration = true,
                UnmappedMemberHandling = XmlUnmappedMemberHandling.Disallow,
            };

            SpikeXmlContext xmlContext = new SpikeXmlContext(serializerOptions);

            FrozenDictionary<string, ISoapOperation> operations = BuildOperationTable(xmlContext);

            WebApplication app = builder.Build();

            app.MapGet("/health", static (HttpContext httpContext) =>
            {
                httpContext.Response.StatusCode = StatusCodes.Status200OK;
                httpContext.Response.Headers.ContentType = "text/plain; charset=utf-8";
                return httpContext.Response.WriteAsync(
                    $"ok echo={Volatile.Read(ref s_echoInvocations)} transfer={Volatile.Read(ref s_transferInvocations)}",
                    httpContext.RequestAborted);
            });

            app.MapPost("/Service.svc", (HttpContext httpContext) => HandleSoapPostAsync(httpContext, operations));

            await app.StartAsync().ConfigureAwait(false);
            TimeSpan startupElapsed = Stopwatch.GetElapsedTime(startTimestamp);
            Console.WriteLine($"R0C_READY urls={string.Join(",", app.Urls)} startupMs={startupElapsed.TotalMilliseconds:F2}");

            await app.WaitForShutdownAsync().ConfigureAwait(false);
            return 0;
        }

        #endregion

        #region Private Methods

        private static FrozenDictionary<string, ISoapOperation> BuildOperationTable(SpikeXmlContext xmlContext)
        {
            Dictionary<string, ISoapOperation> table = new Dictionary<string, ISoapOperation>(2, StringComparer.Ordinal)
            {
                ["urn:echo/IEchoService/Echo"] = new SoapOperation<EchoRequest, EchoResponse>(
                    "urn:echo/IEchoService/Echo",
                    xmlContext.EchoRequest,
                    xmlContext.EchoResponse,
                    static (request, _, _) =>
                    {
                        Interlocked.Increment(ref s_echoInvocations);
                        return ValueTask.FromResult(new EchoResponse { EchoResult = request.Message });
                    }),
                ["urn:payments/IPaymentService/Transfer"] = new SoapOperation<TransferRequestWire, TransferResponseWire>(
                    "urn:payments/IPaymentService/Transfer",
                    xmlContext.TransferRequestWire,
                    xmlContext.TransferResponseWire,
                    static (wire, _, _) =>
                    {
                        long seq = Interlocked.Increment(ref s_transferInvocations);
                        return ValueTask.FromResult(new TransferResponseWire
                        {
                            Result = new TransferResult
                            {
                                TransactionId = $"TX-{seq:D3}",
                                Successful = wire.Request.Amount > 0m,
                            },
                        });
                    }),
            };

            return table.ToFrozenDictionary(StringComparer.Ordinal);
        }

        private static async Task HandleSoapPostAsync(
            HttpContext httpContext,
            FrozenDictionary<string, ISoapOperation> operations)
        {
            CancellationToken ct = httpContext.RequestAborted;

            if (!TryValidateContentType(httpContext.Request.Headers.ContentType))
            {
                httpContext.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                return;
            }

            if (httpContext.Request.ContentLength is long contentLength && contentLength > MaxRequestBytes)
            {
                httpContext.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                return;
            }

            if (!TryGetSoapAction(httpContext.Request.Headers, out string? soapAction))
            {
                await SoapResponseWriter.WriteFaultAsync(
                    httpContext,
                    StatusCodes.Status500InternalServerError,
                    "Client",
                    "Missing or malformed SOAPAction header.",
                    MaxFaultBytes,
                    ct).ConfigureAwait(false);
                return;
            }

            try
            {
                using SoapRequestBuffer requestBuffer = await SoapRequestBuffer.ReadBoundedAsync(
                    httpContext.Request.BodyReader,
                    MaxRequestBytes,
                    4096,
                    clearOnReturn: true,
                    ct).ConfigureAwait(false);

                IPreparedSoapCall preparedCall = SoapEnvelopeReader.ParseAndPrepare(
                    requestBuffer.Span,
                    soapAction,
                    operations,
                    MaxRequestBytes);

                await SoapResponseWriter.WriteSuccessAsync(
                    httpContext,
                    preparedCall,
                    MaxResponseBytes,
                    clearOnReturn: true,
                    ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (RequestQuotaExceededException)
            {
                if (!httpContext.Response.HasStarted)
                {
                    httpContext.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                }
            }
            catch (SoapProtocolException ex)
            {
                if (ex.EmitSoapFault)
                {
                    await SoapResponseWriter.WriteFaultAsync(
                        httpContext,
                        ex.HttpStatusCode,
                        ex.FaultCodeLocalName,
                        ex.Message,
                        MaxFaultBytes,
                        ct).ConfigureAwait(false);
                }
                else if (!httpContext.Response.HasStarted)
                {
                    httpContext.Response.StatusCode = ex.HttpStatusCode;
                    httpContext.Response.Headers.ContentType = "text/plain; charset=utf-8";
                    await httpContext.Response.WriteAsync(ex.Message, ct).ConfigureAwait(false);
                }
            }
            catch (ResponseQuotaExceededException)
            {
                await SoapResponseWriter.WriteFaultAsync(
                    httpContext,
                    StatusCodes.Status500InternalServerError,
                    "Server",
                    "SOAP response exceeded the configured size limit.",
                    MaxFaultBytes,
                    ct).ConfigureAwait(false);
            }
            catch (Exception)
            {
                await SoapResponseWriter.WriteFaultAsync(
                    httpContext,
                    StatusCodes.Status500InternalServerError,
                    "Server",
                    "Internal SOAP processing error.",
                    MaxFaultBytes,
                    ct).ConfigureAwait(false);
            }
        }

        private static bool TryGetSoapAction(IHeaderDictionary headers, [NotNullWhen(true)] out string? soapAction)
        {
            soapAction = null;
            if (!headers.TryGetValue("SOAPAction", out StringValues values) || values.Count != 1)
            {
                return false;
            }

            string? raw = values[0];
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            ReadOnlySpan<char> trimmed = raw.AsSpan().Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            {
                trimmed = trimmed.Slice(1, trimmed.Length - 2);
            }

            if (trimmed.IsEmpty || trimmed.IsWhiteSpace())
            {
                return false;
            }

            soapAction = trimmed.ToString();
            return true;
        }

        private static bool TryValidateContentType(StringValues contentTypeHeader)
        {
            if (contentTypeHeader.Count != 1 || string.IsNullOrWhiteSpace(contentTypeHeader[0]))
            {
                return false;
            }

            if (!MediaTypeHeaderValue.TryParse(contentTypeHeader[0], out MediaTypeHeaderValue? mediaType))
            {
                return false;
            }

            if (!string.Equals(mediaType.MediaType, "text/xml", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (mediaType.CharSet is not null && !string.Equals(mediaType.CharSet.Trim('"'), "utf-8", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        #endregion

    }

}
