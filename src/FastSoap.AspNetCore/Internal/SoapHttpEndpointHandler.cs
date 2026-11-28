using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using FastSoap.Abstractions;
using FastSoap.Core.Buffers;
using FastSoap.Core.Dispatching;
using FastSoap.Core.Protocol;
using FastSoap.Core.Serialization;
using FastSoap.Metadata;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace FastSoap.AspNetCore.Internal
{

    internal sealed class SoapHttpEndpointHandler
    {

        #region Fields

        private static readonly Func<object?, string, CancellationToken, ValueTask> StartDirectResponseCallback = StartDirectResponseAsync;
        private static readonly Func<object?, string, CancellationToken, ValueTask> StartDirectResponseWithVaryCallback = StartDirectResponseWithVaryAsync;

        private readonly BuiltSoapEndpoint _endpoint;
        private readonly string _endpointPattern;
        private readonly ILogger _logger;

        #endregion

        #region Constructors

        internal SoapHttpEndpointHandler(
            string endpointPattern,
            BuiltSoapEndpoint endpoint,
            ILogger logger)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(endpointPattern);
            ArgumentNullException.ThrowIfNull(endpoint);
            ArgumentNullException.ThrowIfNull(logger);

            _endpointPattern = endpointPattern;
            _endpoint = endpoint;
            _logger = logger;
        }

        #endregion

        #region Internal Methods

        internal async Task HandleMetadataGetAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            CancellationToken ct = httpContext.RequestAborted;
            if (ct.IsCancellationRequested)
            {
                return;
            }

            SoapStaticMetadataProvider? provider = _endpoint.MetadataProvider;
            if (!_endpoint.Options.EnableMetadata || provider is null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                httpContext.Response.ContentLength = 0;

                return;
            }

            IQueryCollection query = httpContext.Request.Query;
            bool hasWsdl = query.TryGetValue("wsdl", out StringValues wsdlValues);
            bool hasXsd = query.TryGetValue("xsd", out StringValues xsdValues);

            if (hasWsdl && hasXsd)
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                httpContext.Response.ContentLength = 0;

                return;
            }

            SoapMetadataDocument? document;
            if (hasWsdl)
            {
                if (query.Count != 1 || wsdlValues.Count != 1 || !string.IsNullOrEmpty(wsdlValues[0]))
                {
                    httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                    httpContext.Response.ContentLength = 0;

                    return;
                }

                document = provider.GetWsdl();
            }
            else if (hasXsd)
            {
                if (query.Count != 1 || xsdValues.Count != 1)
                {
                    httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                    httpContext.Response.ContentLength = 0;

                    return;
                }

                string? schemaName = xsdValues[0];
                if (!SoapStaticMetadataProvider.TryNormalizeSchemaName(schemaName, out _))
                {
                    httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                    httpContext.Response.ContentLength = 0;

                    return;
                }

                if (!provider.TryGetSchema(schemaName, out document))
                {
                    httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                    httpContext.Response.ContentLength = 0;

                    return;
                }
            }
            else
            {
                httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                httpContext.Response.ContentLength = 0;

                return;
            }

            HttpResponse response = httpContext.Response;
            response.Headers.ETag = document.ETag;

            StringValues ifNoneMatch = httpContext.Request.Headers.IfNoneMatch;
            for (int index = 0; index < ifNoneMatch.Count; index++)
            {
                string? candidate = ifNoneMatch[index];
                if (candidate is not null && document.MatchesETag(candidate.AsSpan()))
                {
                    response.StatusCode = StatusCodes.Status304NotModified;

                    return;
                }
            }

            response.StatusCode = StatusCodes.Status200OK;
            response.Headers.ContentType = document.ContentType;
            response.ContentLength = document.ContentUtf8.Length;

            if (!document.ContentUtf8.IsEmpty)
            {
                BuffersExtensions.Write(response.BodyWriter, document.ContentUtf8.Span);
            }

            FlushResult flushResult = await response.BodyWriter.FlushAsync(ct).ConfigureAwait(false);
            if (flushResult.IsCanceled)
            {
                return;
            }
        }

        internal async Task HandlePostAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            CancellationToken ct = httpContext.RequestAborted;
            long startTimestamp = Stopwatch.GetTimestamp();
            SoapEndpointOptions options = _endpoint.Options;

            using Activity? activity = FastSoapTelemetry.ActivitySource.StartActivity(
                "FastSoap.Process",
                ActivityKind.Internal);

            if (activity is not null)
            {
                activity.SetTag("endpoint", _endpointPattern);
            }

            TagList activeTags = default;
            activeTags.Add("endpoint", _endpointPattern);
            FastSoapTelemetry.ActiveRequests.Add(1, activeTags);

            string operationTag = "unknown";
            string versionTag = "unknown";
            string faultKindTag = "none";
            string outcomeTag = "http_error";
            int statusCode = StatusCodes.Status500InternalServerError;
            long requestByteCount = 0;
            long responseByteCount = 0;

            try
            {
                if (ct.IsCancellationRequested)
                {
                    outcomeTag = "canceled";
                    RecordCancellation(activity);

                    return;
                }

                HttpRequest request = httpContext.Request;
                HttpResponse response = httpContext.Response;

                if (!TryResolveRequestEncoding(
                    request.Headers.ContentEncoding,
                    options.AllowCompressedRequests,
                    out SoapCompressionEncoding requestEncoding))
                {
                    statusCode = StatusCodes.Status415UnsupportedMediaType;
                    WriteHeaderOnlyStatus(response, statusCode);

                    return;
                }

                try
                {
                    SoapRequestBuffer.ValidateContentLength(request.ContentLength, options.MaxRequestBytes);
                }
                catch (RequestQuotaExceededException)
                {
                    statusCode = StatusCodes.Status413PayloadTooLarge;
                    outcomeTag = "quota_rejected";
                    RecordQuotaRejection(activity, options.MaxRequestBytes);
                    WriteHeaderOnlyStatus(response, statusCode);

                    return;
                }

                StringValues contentTypeHeaders = request.Headers.ContentType;
                if (contentTypeHeaders.Count > 1)
                {
                    statusCode = StatusCodes.Status400BadRequest;
                    WriteHeaderOnlyStatus(response, statusCode);

                    return;
                }

                string? rawContentType = contentTypeHeaders.Count == 1 ? contentTypeHeaders[0] : null;
                SoapContentTypeInfo contentTypeInfo;
                try
                {
                    contentTypeInfo = SoapContentTypeParser.Parse(
                        rawContentType,
                        options.EnabledVersions,
                        options.MaxActionLength);
                }
                catch (SoapProtocolException ex)
                {
                    statusCode = (int)ex.HttpStatusCode;
                    if (_logger.IsEnabled(LogLevel.Warning))
                    {
                        FastSoapLogMessages.ProtocolRejection(
                            _logger,
                            _endpointPattern,
                            versionTag,
                            statusCode,
                            faultKindTag,
                            ex.PublicReason);
                    }

                    WriteHeaderOnlyStatus(response, statusCode);

                    return;
                }

                versionTag = FastSoapTelemetry.FormatVersionTag(contentTypeInfo.Version);

                StringValues soapActionHeaders = request.Headers[SoapConstants.SoapActionHeaderName];
                bool hasMultipleSoapActions = soapActionHeaders.Count > 1;
                string? rawSoapAction = soapActionHeaders.Count == 1 ? soapActionHeaders[0] : null;
                operationTag = ResolveOperationNameForTelemetry(contentTypeInfo, rawSoapAction, hasMultipleSoapActions);

                IHttpMaxRequestBodySizeFeature? maxBodyFeature = httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (maxBodyFeature is not null && !maxBodyFeature.IsReadOnly)
                {
                    long existingMax = maxBodyFeature.MaxRequestBodySize ?? long.MaxValue;
                    maxBodyFeature.MaxRequestBodySize = Math.Min(existingMax, options.MaxRequestBytes);
                }

                using SoapRequestBuffer requestBuffer = await ReadBoundedRequestAsync(
                    request,
                    options,
                    requestEncoding,
                    ct).ConfigureAwait(false);

                requestByteCount = requestBuffer.Length;
                FastSoapTelemetry.RequestBytes.Record(
                    requestByteCount,
                    new KeyValuePair<string, object?>("endpoint", _endpointPattern),
                    new KeyValuePair<string, object?>("soap.version", versionTag));

                SoapCompressionEncoding responseEncoding = NegotiateResponseEncoding(request.Headers.AcceptEncoding, options);
                bool useDirectStreaming = options.EnableDirectResponseStreaming &&
                    responseEncoding == SoapCompressionEncoding.Identity;

                using SoapProcessingResult result = useDirectStreaming
                    ? await _endpoint.MessageProcessor.ProcessParsedDirectAsync(
                        contentTypeInfo,
                        rawSoapAction,
                        hasMultipleSoapActions,
                        httpContext.TraceIdentifier,
                        requestBuffer.Memory,
                        httpContext.RequestServices,
                        httpContext.User,
                        response.BodyWriter,
                        options.EnableResponseCompression
                            ? StartDirectResponseWithVaryCallback
                            : StartDirectResponseCallback,
                        response,
                        ct).ConfigureAwait(false)
                    : await _endpoint.MessageProcessor.ProcessParsedAsync(
                        contentTypeInfo,
                        rawSoapAction,
                        hasMultipleSoapActions,
                        httpContext.TraceIdentifier,
                        requestBuffer.Memory,
                        httpContext.RequestServices,
                        httpContext.User,
                        ct).ConfigureAwait(false);

                statusCode = result.StatusCode;
                faultKindTag = FastSoapTelemetry.FormatFaultKindTag(result.FaultKind);
                outcomeTag = DetermineOutcomeTag(result);
                responseByteCount = result.IsDirectStreamed ? result.DirectStreamedBytes : result.Body.Length;

                if (result.UnhandledException is not null)
                {
                    FastSoapLogMessages.UnhandledProcessingFault(
                        _logger,
                        _endpointPattern,
                        operationTag,
                        versionTag,
                        statusCode,
                        faultKindTag,
                        result.FailureStage ?? "unknown",
                        result.UnhandledException);
                }

                if (result.FaultKind is not null)
                {
                    FastSoapTelemetry.FaultsTotal.Add(
                        1,
                        new KeyValuePair<string, object?>("endpoint", _endpointPattern),
                        new KeyValuePair<string, object?>("operation", operationTag),
                        new KeyValuePair<string, object?>("soap.version", versionTag),
                        new KeyValuePair<string, object?>("fault.kind", faultKindTag));

                    if (result.UnhandledException is null && _logger.IsEnabled(LogLevel.Warning))
                    {
                        FastSoapLogMessages.ProtocolRejection(
                            _logger,
                            _endpointPattern,
                            versionTag,
                            statusCode,
                            faultKindTag,
                            "SOAP fault staged for client.");
                    }
                }

                if (result.IsDirectStreamed)
                {
                    if (responseByteCount > 0)
                    {
                        FastSoapTelemetry.ResponseBytes.Record(
                            responseByteCount,
                            new KeyValuePair<string, object?>("endpoint", _endpointPattern),
                            new KeyValuePair<string, object?>("operation", operationTag),
                            new KeyValuePair<string, object?>("soap.version", versionTag));
                    }

                    return;
                }

                if (response.HasStarted || ct.IsCancellationRequested)
                {
                    if (ct.IsCancellationRequested)
                    {
                        outcomeTag = "canceled";
                        RecordCancellation(activity);
                    }

                    return;
                }

                response.StatusCode = result.StatusCode;
                if (result.ContentType is not null)
                {
                    response.Headers.ContentType = result.ContentType;
                }

                if (!result.Body.IsEmpty)
                {
                    if (options.EnableResponseCompression)
                    {
                        AppendVaryAcceptEncoding(response);
                    }

                    bool sentCompressed = false;
                    if (options.EnableResponseCompression &&
                        responseEncoding != SoapCompressionEncoding.Identity &&
                        result.Body.Length >= options.ResponseCompressionMinBytes)
                    {
                        int initialCompressedCapacity = Math.Min(Math.Max(512, result.Body.Length), options.MaxResponseBytes);
                        using BoundedPooledBufferWriter compressedWriter = new BoundedPooledBufferWriter(
                            initialCompressedCapacity,
                            options.MaxResponseBytes,
                            options.ClearPooledBuffers,
                            _endpoint.BufferPool);

                        bool compressionFitsAndShrinks = false;
                        try
                        {
                            SoapCompressionCodec.Compress(result.Body.Span, responseEncoding, compressedWriter);
                            compressionFitsAndShrinks = compressedWriter.WrittenCount < result.Body.Length;
                        }
                        catch (ResponseQuotaExceededException)
                        {
                            compressionFitsAndShrinks = false;
                        }

                        if (compressionFitsAndShrinks)
                        {
                            response.Headers.ContentEncoding = SoapCompressionCodec.ToHeaderValue(responseEncoding);
                            responseByteCount = compressedWriter.WrittenCount;
                            response.ContentLength = responseByteCount;

                            await compressedWriter.CommitToAsync(response.BodyWriter, ct).ConfigureAwait(false);
                            sentCompressed = true;
                        }
                    }

                    if (!sentCompressed)
                    {
                        response.ContentLength = result.Body.Length;
                        await result.CommitToAsync(response.BodyWriter, ct).ConfigureAwait(false);
                    }

                    FastSoapTelemetry.ResponseBytes.Record(
                        responseByteCount,
                        new KeyValuePair<string, object?>("endpoint", _endpointPattern),
                        new KeyValuePair<string, object?>("operation", operationTag),
                        new KeyValuePair<string, object?>("soap.version", versionTag));
                }
                else
                {
                    response.ContentLength = 0;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                outcomeTag = "canceled";
                statusCode = 499;
                RecordCancellation(activity);
            }
            catch (RequestQuotaExceededException) when (!httpContext.Response.HasStarted)
            {
                statusCode = StatusCodes.Status413PayloadTooLarge;
                outcomeTag = "quota_rejected";
                RecordQuotaRejection(activity, options.MaxRequestBytes);

                if (!ct.IsCancellationRequested)
                {
                    WriteHeaderOnlyStatus(httpContext.Response, statusCode);
                }
            }
            catch (InvalidDataException) when (!httpContext.Response.HasStarted)
            {
                statusCode = StatusCodes.Status400BadRequest;
                outcomeTag = "http_error";

                if (!ct.IsCancellationRequested)
                {
                    WriteHeaderOnlyStatus(httpContext.Response, statusCode);
                }
            }
            catch (BadHttpRequestException ex) when (!httpContext.Response.HasStarted && ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
            {
                statusCode = StatusCodes.Status413PayloadTooLarge;
                outcomeTag = "quota_rejected";
                RecordQuotaRejection(activity, options.MaxRequestBytes);

                if (!ct.IsCancellationRequested)
                {
                    WriteHeaderOnlyStatus(httpContext.Response, statusCode);
                }
            }
            catch (BadHttpRequestException) when (!httpContext.Response.HasStarted)
            {
                statusCode = StatusCodes.Status400BadRequest;
                outcomeTag = "http_error";

                if (!ct.IsCancellationRequested)
                {
                    WriteHeaderOnlyStatus(httpContext.Response, statusCode);
                }
            }
            catch (Exception ex)
            {
                statusCode = StatusCodes.Status500InternalServerError;
                outcomeTag = "fault";
                faultKindTag = "receiver";

                if (httpContext.Response.HasStarted || ct.IsCancellationRequested)
                {
                    FastSoapLogMessages.ResponseCommitFailedAfterStart(_logger, _endpointPattern, ex);
                    httpContext.Abort();
                }
                else
                {
                    FastSoapLogMessages.OperationFailed(_logger, _endpointPattern, operationTag, versionTag, ex);
                    await WriteSafeFallbackFaultAsync(httpContext.Response, versionTag, options, ct).ConfigureAwait(false);
                }
            }
            finally
            {
                FastSoapTelemetry.ActiveRequests.Add(-1, activeTags);

                double elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
                string statusClassTag = FastSoapTelemetry.FormatStatusClassTag(statusCode);

                TagList finalTags = default;
                finalTags.Add("endpoint", _endpointPattern);
                finalTags.Add("operation", operationTag);
                finalTags.Add("soap.version", versionTag);
                finalTags.Add("fault.kind", faultKindTag);
                finalTags.Add("outcome", outcomeTag);
                finalTags.Add("http.status_class", statusClassTag);

                FastSoapTelemetry.RequestsTotal.Add(1, finalTags);
                FastSoapTelemetry.RequestDurationMs.Record(elapsedMs, finalTags);

                if (activity is not null)
                {
                    activity.SetTag("operation", operationTag);
                    activity.SetTag("soap.version", versionTag);
                    activity.SetTag("fault.kind", faultKindTag);
                    activity.SetTag("outcome", outcomeTag);
                    activity.SetTag("http.status_class", statusClassTag);

                    if (string.Equals(outcomeTag, "fault", StringComparison.Ordinal) ||
                        string.Equals(outcomeTag, "http_error", StringComparison.Ordinal) ||
                        string.Equals(outcomeTag, "quota_rejected", StringComparison.Ordinal))
                    {
                        activity.SetStatus(ActivityStatusCode.Error, outcomeTag);
                    }
                }

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    FastSoapLogMessages.RequestCompleted(
                        _logger,
                        _endpointPattern,
                        operationTag,
                        versionTag,
                        statusCode,
                        outcomeTag);
                }
            }
        }

        #endregion

        #region Private Methods

        private static void AppendVaryAcceptEncoding(HttpResponse response)
        {
            StringValues existing = response.Headers.Vary;
            if (existing.Count == 0)
            {
                response.Headers.Vary = HeaderNames.AcceptEncoding;

                return;
            }

            for (int index = 0; index < existing.Count; index++)
            {
                string? value = existing[index];
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                ReadOnlySpan<char> span = value.AsSpan();
                foreach (Range tokenRange in span.Split(','))
                {
                    if (span[tokenRange].Trim().Equals(HeaderNames.AcceptEncoding, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }
            }

            response.Headers.Vary = StringValues.Concat(existing, HeaderNames.AcceptEncoding);
        }

        private static string DetermineOutcomeTag(SoapProcessingResult result)
        {
            if (result.FaultKind is not null)
            {
                return "fault";
            }

            if (result.StatusCode >= 200 && result.StatusCode < 300)
            {
                return "success";
            }

            return "http_error";
        }

        private static SoapCompressionEncoding NegotiateResponseEncoding(
            StringValues acceptEncodingHeaders,
            SoapEndpointOptions options)
        {
            if (!options.EnableResponseCompression || acceptEncodingHeaders.Count == 0)
            {
                return SoapCompressionEncoding.Identity;
            }

            if (acceptEncodingHeaders.Count == 1)
            {
                return SoapCompressionCodec.NegotiateAcceptEncoding(acceptEncodingHeaders[0].AsSpan());
            }

            return SoapCompressionCodec.NegotiateAcceptEncoding(acceptEncodingHeaders.ToString().AsSpan());
        }

        private async ValueTask<SoapRequestBuffer> ReadBoundedRequestAsync(
            HttpRequest request,
            SoapEndpointOptions options,
            SoapCompressionEncoding requestEncoding,
            CancellationToken ct)
        {
            return await SoapRequestBuffer.CopyFromPipeAsync(
                request.BodyReader,
                options.MaxRequestBytes,
                requestEncoding,
                initialCapacity: 4096,
                clearOnReturn: options.ClearPooledBuffers,
                pool: _endpoint.BufferPool,
                cancellationToken: ct).ConfigureAwait(false);
        }

        private void RecordCancellation(Activity? activity)
        {
            FastSoapTelemetry.CancellationsTotal.Add(
                1,
                new KeyValuePair<string, object?>("endpoint", _endpointPattern));

            if (activity is not null)
            {
                activity.SetTag("outcome", "canceled");
            }

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                FastSoapLogMessages.RequestAborted(_logger, _endpointPattern);
            }
        }

        private void RecordQuotaRejection(Activity? activity, int maxRequestBytes)
        {
            FastSoapTelemetry.QuotaRejectionsTotal.Add(
                1,
                new KeyValuePair<string, object?>("endpoint", _endpointPattern));

            if (activity is not null)
            {
                activity.SetTag("outcome", "quota_rejected");
            }

            if (_logger.IsEnabled(LogLevel.Warning))
            {
                FastSoapLogMessages.QuotaRejected(_logger, _endpointPattern, maxRequestBytes);
            }
        }

        private string ResolveOperationNameForTelemetry(
            SoapContentTypeInfo contentTypeInfo,
            string? rawSoapAction,
            bool hasMultipleSoapActions)
        {
            ReadOnlySpan<char> actionSpan;
            if (contentTypeInfo.Version == SoapVersion.Soap12)
            {
                if (string.IsNullOrWhiteSpace(contentTypeInfo.Action))
                {
                    return "unknown";
                }

                actionSpan = contentTypeInfo.Action.AsSpan();
            }
            else
            {
                if (hasMultipleSoapActions || string.IsNullOrWhiteSpace(rawSoapAction))
                {
                    return "unknown";
                }

                actionSpan = rawSoapAction.AsSpan().Trim();
                if (actionSpan.Length >= 2 && actionSpan[0] == '"' && actionSpan[^1] == '"')
                {
                    actionSpan = actionSpan[1..^1];
                }

                if (actionSpan.IsEmpty || actionSpan.IsWhiteSpace())
                {
                    return "unknown";
                }
            }

            if (_endpoint.Operations.TryGetByAction(actionSpan, out ISoapOperation? operation))
            {
                return operation.Descriptor.Name;
            }

            return "unknown";
        }

        private static async ValueTask StartDirectResponseAsync(
            object? state,
            string responseContentType,
            CancellationToken ct)
        {
            HttpResponse response = (HttpResponse)state!;
            response.StatusCode = StatusCodes.Status200OK;
            response.Headers.ContentType = responseContentType;

            await response.StartAsync(ct).ConfigureAwait(false);
        }

        private static async ValueTask StartDirectResponseWithVaryAsync(
            object? state,
            string responseContentType,
            CancellationToken ct)
        {
            HttpResponse response = (HttpResponse)state!;
            response.StatusCode = StatusCodes.Status200OK;
            response.Headers.ContentType = responseContentType;
            AppendVaryAcceptEncoding(response);

            await response.StartAsync(ct).ConfigureAwait(false);
        }

        private static bool TryResolveRequestEncoding(
            StringValues contentEncodingHeaders,
            bool allowCompressedRequests,
            out SoapCompressionEncoding encoding)
        {
            if (contentEncodingHeaders.Count == 0)
            {
                encoding = SoapCompressionEncoding.Identity;

                return true;
            }

            if (contentEncodingHeaders.Count > 1)
            {
                encoding = SoapCompressionEncoding.Identity;

                return false;
            }

            return SoapCompressionCodec.TryParseRequestContentEncoding(
                contentEncodingHeaders[0],
                allowCompressedRequests,
                out encoding);
        }

        private static void WriteHeaderOnlyStatus(HttpResponse response, int statusCode)
        {
            if (response.HasStarted)
            {
                return;
            }

            response.StatusCode = statusCode;
            response.ContentLength = 0;
        }

        private async ValueTask WriteSafeFallbackFaultAsync(
            HttpResponse response,
            string versionTag,
            SoapEndpointOptions options,
            CancellationToken ct)
        {
            if (response.HasStarted || ct.IsCancellationRequested)
            {
                return;
            }

            SoapVersion faultVersion = string.Equals(versionTag, "soap12", StringComparison.Ordinal)
                ? SoapVersion.Soap12
                : SoapVersion.Soap11;

            try
            {
                int faultLimit = Math.Min(SoapConstants.DefaultFaultBytesLimit, options.MaxResponseBytes);
                using BoundedPooledBufferWriter writer = new BoundedPooledBufferWriter(
                    512,
                    faultLimit,
                    options.ClearPooledBuffers,
                    _endpoint.BufferPool);

                SoapFault safeFault = SoapFault.CreateStandard(
                    faultVersion,
                    SoapFaultKind.Receiver,
                    "An internal server error occurred while processing the SOAP request.");

                SoapFaultWriter.WriteFault(safeFault, writer);

                response.StatusCode = StatusCodes.Status500InternalServerError;
                response.Headers.ContentType = faultVersion == SoapVersion.Soap11
                    ? SoapConstants.Soap11ResponseContentType
                    : SoapConstants.Soap12ResponseContentType;
                response.ContentLength = writer.WrittenCount;

                await writer.CommitToAsync(response.BodyWriter, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                WriteHeaderOnlyStatus(response, StatusCodes.Status500InternalServerError);
            }
        }

        #endregion

    }

}
