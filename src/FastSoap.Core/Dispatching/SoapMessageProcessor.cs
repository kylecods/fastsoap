using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using FastSoap.Abstractions;
using FastSoap.Core.Buffers;
using FastSoap.Core.Protocol;
using FastSoap.Core.Serialization;

namespace FastSoap.Core.Dispatching
{

    /// <summary>
    /// Captures transport-independent HTTP request metadata required by <see cref="SoapMessageProcessor"/>.
    /// </summary>
    /// <param name="ContentType">The HTTP <c>Content-Type</c> header value.</param>
    /// <param name="SoapAction">The HTTP <c>SOAPAction</c> header value, if present.</param>
    /// <param name="HasMultipleSoapActionHeaders">Whether multiple <c>SOAPAction</c> headers were sent.</param>
    /// <param name="ContentEncoding">The HTTP <c>Content-Encoding</c> header value, if present.</param>
    /// <param name="CorrelationId">An optional correlation identifier for fault metadata.</param>
    public readonly record struct SoapRequestMetadata(
        string? ContentType,
        string? SoapAction,
        bool HasMultipleSoapActionHeaders = false,
        string? ContentEncoding = null,
        string? CorrelationId = null);

    /// <summary>
    /// Represents the owned HTTP status, content type, and staged UTF-8 response payload produced by
    /// <see cref="SoapMessageProcessor"/>.
    /// </summary>
    /// <remarks>
    /// Callers must dispose this instance in a <see langword="finally"/> / <see langword="using"/> block to return
    /// any pooled staging buffer.
    /// </remarks>
    public sealed class SoapProcessingResult : IDisposable
    {

        #region Fields

        private bool _disposed;
        private BoundedPooledBufferWriter? _stagedWriter;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the staged UTF-8 response body bytes, or an empty memory slice for header-only HTTP errors.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown when the result has been disposed.</exception>
        public ReadOnlyMemory<byte> Body
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                return _stagedWriter is not null
                    ? _stagedWriter.WrittenMemory
                    : field;
            }
        }

        /// <summary>
        /// Gets the response HTTP <c>Content-Type</c> header value, or <see langword="null"/> when empty.
        /// </summary>
        public string? ContentType { get; }

        /// <summary>
        /// Gets the number of UTF-8 bytes written when <see cref="IsDirectStreamed"/> is <see langword="true"/>.
        /// </summary>
        public long DirectStreamedBytes { get; }

        /// <summary>
        /// Gets the optional internal failure stage tag when <see cref="UnhandledException"/> is non-null.
        /// </summary>
        public string? FailureStage { get; }

        /// <summary>
        /// Gets the normalized SOAP fault classification if a SOAP fault was produced; otherwise <see langword="null"/>.
        /// </summary>
        public SoapFaultKind? FaultKind { get; }

        /// <summary>
        /// Gets a value indicating whether the response was streamed directly to the transport <see cref="PipeWriter"/>
        /// without a staging buffer.
        /// </summary>
        public bool IsDirectStreamed { get; }

        /// <summary>
        /// Gets a value indicating whether the business operation handler was invoked.
        /// </summary>
        public bool ServiceInvoked { get; }

        /// <summary>
        /// Gets the HTTP status code to emit for this result.
        /// </summary>
        public int StatusCode { get; }

        /// <summary>
        /// Gets the underlying unhandled exception when a sanitized internal fault was staged; otherwise <see langword="null"/>.
        /// </summary>
        public Exception? UnhandledException { get; }

        #endregion

        #region Constructors

        internal SoapProcessingResult(
            int statusCode,
            string? contentType,
            ReadOnlyMemory<byte> body,
            BoundedPooledBufferWriter? stagedWriter,
            bool serviceInvoked,
            SoapFaultKind? faultKind,
            Exception? unhandledException = null,
            string? failureStage = null,
            bool isDirectStreamed = false,
            long directStreamedBytes = 0)
        {
            StatusCode = statusCode;
            ContentType = contentType;
            Body = body;
            _stagedWriter = stagedWriter;
            ServiceInvoked = serviceInvoked;
            FaultKind = faultKind;
            UnhandledException = unhandledException;
            FailureStage = failureStage;
            IsDirectStreamed = isDirectStreamed;
            DirectStreamedBytes = directStreamedBytes;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Writes the staged response body to <paramref name="destination"/> using
        /// <see cref="BuffersExtensions.Write{T}(IBufferWriter{T}, ReadOnlySpan{T})"/> and flushes the pipe.
        /// </summary>
        /// <param name="destination">The downstream <see cref="PipeWriter"/>.</param>
        /// <param name="cancellationToken">A cancellation token monitored during flushing.</param>
        /// <returns>A <see cref="ValueTask"/> representing the asynchronous write and flush operation.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the result has been disposed.</exception>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="destination"/> is <see langword="null"/>.</exception>
        public async ValueTask CommitToAsync(PipeWriter destination, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(destination);

            if (_stagedWriter is not null)
            {
                await _stagedWriter.CommitToAsync(destination, cancellationToken).ConfigureAwait(false);

                return;
            }

            if (!Body.IsEmpty)
            {
                BuffersExtensions.Write(destination, Body.Span);
            }

            FlushResult result = await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            if (result.IsCanceled)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }

        /// <summary>
        /// Releases any owned response staging buffer back to its originating pool exactly once.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            BoundedPooledBufferWriter? writer = _stagedWriter;
            _stagedWriter = null;
            writer?.Dispose();
        }

        #endregion

    }

    /// <summary>
    /// End-to-end, hosting-independent SOAP 1.1 and SOAP 1.2 message processor executing protocol validation,
    /// synchronous envelope parsing, asynchronous header processing, typed invocation, and bounded response/fault staging.
    /// </summary>
    /// <remarks>
    /// Contains zero ASP.NET Core dependencies per ADR-010 so protocol execution can be tested deterministically
    /// without Kestrel and integrated cleanly by <c>FastSoap.AspNetCore</c>.
    /// </remarks>
    public sealed class SoapMessageProcessor
    {

        #region Fields

        private readonly SoapHeaderProcessor _headerProcessor;
        private readonly SoapOperationTable _operations;
        private readonly SoapEndpointOptions _options;
        private readonly ArrayPool<byte> _pool;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new <see cref="SoapMessageProcessor"/> for an endpoint.
        /// </summary>
        /// <param name="options">The validated endpoint configuration options.</param>
        /// <param name="operations">The immutable operation dispatch table.</param>
        /// <param name="headerProcessor">Optional explicit header processor; defaults to <see cref="SoapHeaderProcessor.Empty"/>.</param>
        /// <param name="pool">Optional <see cref="ArrayPool{T}"/> for response/fault staging; defaults to <see cref="ArrayPool{T}.Shared"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> or <paramref name="operations"/> is <see langword="null"/>.</exception>
        public SoapMessageProcessor(
            SoapEndpointOptions options,
            SoapOperationTable operations,
            SoapHeaderProcessor? headerProcessor = null,
            ArrayPool<byte>? pool = null)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(operations);

            options.Validate();

            _options = options;
            _operations = operations;
            _headerProcessor = headerProcessor ?? SoapHeaderProcessor.Empty;
            _pool = pool ?? ArrayPool<byte>.Shared;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Processes a complete bounded SOAP request payload and produces a staged <see cref="SoapProcessingResult"/>.
        /// </summary>
        /// <param name="metadata">Transport-independent HTTP request metadata.</param>
        /// <param name="requestUtf8">Complete bounded UTF-8 request XML bytes.</param>
        /// <param name="services">The request-scoped <see cref="IServiceProvider"/>.</param>
        /// <param name="user">The authenticated <see cref="ClaimsPrincipal"/> for the request.</param>
        /// <param name="cancellationToken">A cancellation token tied to the request.</param>
        /// <returns>An owned <see cref="SoapProcessingResult"/> containing the status, headers, and staged body.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="user"/> is <see langword="null"/>.</exception>
        public ValueTask<SoapProcessingResult> ProcessAsync(
            SoapRequestMetadata metadata,
            ReadOnlyMemory<byte> requestUtf8,
            IServiceProvider services,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(user);

            cancellationToken.ThrowIfCancellationRequested();

            if (!SoapCompressionCodec.TryParseRequestContentEncoding(
                metadata.ContentEncoding,
                _options.AllowCompressedRequests,
                out SoapCompressionEncoding requestEncoding))
            {
                return ValueTask.FromResult(CreateHttpOnlyError(HttpStatusCode.UnsupportedMediaType, serviceInvoked: false));
            }

            if (requestUtf8.Length > _options.MaxRequestBytes)
            {
                return ValueTask.FromResult(CreateHttpOnlyError(HttpStatusCode.RequestEntityTooLarge, serviceInvoked: false));
            }

            SoapContentTypeInfo contentTypeInfo;
            try
            {
                contentTypeInfo = SoapContentTypeParser.Parse(
                    metadata.ContentType,
                    _options.EnabledVersions,
                    _options.MaxActionLength);
            }
            catch (SoapProtocolException ex)
            {
                return ValueTask.FromResult(CreateHttpOnlyError(ex.HttpStatusCode, serviceInvoked: false));
            }

            if (requestEncoding != SoapCompressionEncoding.Identity)
            {
                return ProcessCompressedAsync(
                    contentTypeInfo,
                    metadata.SoapAction,
                    metadata.HasMultipleSoapActionHeaders,
                    metadata.CorrelationId,
                    requestUtf8,
                    requestEncoding,
                    services,
                    user,
                    cancellationToken);
            }

            return ProcessCoreAsync(
                contentTypeInfo,
                metadata.SoapAction,
                metadata.HasMultipleSoapActionHeaders,
                metadata.CorrelationId,
                requestUtf8,
                services,
                user,
                directResponseWriter: null,
                startResponseAsync: null,
                startResponseState: null,
                cancellationToken);
        }

        #endregion

        #region Internal Methods

        internal ValueTask<SoapProcessingResult> ProcessParsedAsync(
            SoapContentTypeInfo contentTypeInfo,
            string? soapAction,
            bool hasMultipleSoapActionHeaders,
            string? correlationId,
            ReadOnlyMemory<byte> requestUtf8,
            IServiceProvider services,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(user);

            cancellationToken.ThrowIfCancellationRequested();

            if (requestUtf8.Length > _options.MaxRequestBytes)
            {
                return ValueTask.FromResult(CreateHttpOnlyError(HttpStatusCode.RequestEntityTooLarge, serviceInvoked: false));
            }

            return ProcessCoreAsync(
                contentTypeInfo,
                soapAction,
                hasMultipleSoapActionHeaders,
                correlationId,
                requestUtf8,
                services,
                user,
                directResponseWriter: null,
                startResponseAsync: null,
                startResponseState: null,
                cancellationToken);
        }

        internal ValueTask<SoapProcessingResult> ProcessParsedDirectAsync(
            SoapContentTypeInfo contentTypeInfo,
            string? soapAction,
            bool hasMultipleSoapActionHeaders,
            string? correlationId,
            ReadOnlyMemory<byte> requestUtf8,
            IServiceProvider services,
            ClaimsPrincipal user,
            PipeWriter directResponseWriter,
            Func<object?, string, CancellationToken, ValueTask> startResponseAsync,
            object? startResponseState,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(directResponseWriter);
            ArgumentNullException.ThrowIfNull(startResponseAsync);

            cancellationToken.ThrowIfCancellationRequested();

            if (requestUtf8.Length > _options.MaxRequestBytes)
            {
                return ValueTask.FromResult(CreateHttpOnlyError(HttpStatusCode.RequestEntityTooLarge, serviceInvoked: false));
            }

            return ProcessCoreAsync(
                contentTypeInfo,
                soapAction,
                hasMultipleSoapActionHeaders,
                correlationId,
                requestUtf8,
                services,
                user,
                directResponseWriter,
                startResponseAsync,
                startResponseState,
                cancellationToken);
        }

        #endregion

        #region Private Methods

        private static int GetFaultStatusCode(SoapVersion version, SoapFaultKind faultKind)
        {
            if (version == SoapVersion.Soap11)
            {
                return (int)HttpStatusCode.InternalServerError;
            }

            return faultKind == SoapFaultKind.Sender
                ? (int)HttpStatusCode.BadRequest
                : (int)HttpStatusCode.InternalServerError;
        }

        private async ValueTask<SoapProcessingResult> ProcessCompressedAsync(
            SoapContentTypeInfo contentTypeInfo,
            string? soapAction,
            bool hasMultipleSoapActionHeaders,
            string? correlationId,
            ReadOnlyMemory<byte> compressedRequest,
            SoapCompressionEncoding requestEncoding,
            IServiceProvider services,
            ClaimsPrincipal user,
            CancellationToken cancellationToken)
        {
            SoapRequestBuffer decompressed;
            try
            {
                decompressed = SoapCompressionCodec.Decompress(
                    compressedRequest,
                    requestEncoding,
                    _options.MaxRequestBytes,
                    _options.ClearPooledBuffers,
                    _pool);
            }
            catch (RequestQuotaExceededException)
            {
                return CreateHttpOnlyError(HttpStatusCode.RequestEntityTooLarge, serviceInvoked: false);
            }
            catch (InvalidDataException)
            {
                return CreateHttpOnlyError(HttpStatusCode.BadRequest, serviceInvoked: false);
            }

            using (decompressed)
            {
                return await ProcessCoreAsync(
                    contentTypeInfo,
                    soapAction,
                    hasMultipleSoapActionHeaders,
                    correlationId,
                    decompressed.Memory,
                    services,
                    user,
                    directResponseWriter: null,
                    startResponseAsync: null,
                    startResponseState: null,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        private async ValueTask<SoapProcessingResult> ProcessCoreAsync(
            SoapContentTypeInfo contentTypeInfo,
            string? soapAction,
            bool hasMultipleSoapActionHeaders,
            string? correlationId,
            ReadOnlyMemory<byte> requestUtf8,
            IServiceProvider services,
            ClaimsPrincipal user,
            PipeWriter? directResponseWriter,
            Func<object?, string, CancellationToken, ValueTask>? startResponseAsync,
            object? startResponseState,
            CancellationToken cancellationToken)
        {
            PreparedSoapCall preparedCall;
            try
            {
                bool hasAction;
                ReadOnlySpan<char> actionSpan;
                if (contentTypeInfo.Version == SoapVersion.Soap11)
                {
                    actionSpan = SoapActionParser.ParseSoap11ActionSpan(
                        soapAction,
                        hasMultipleSoapActionHeaders,
                        _options.AllowMissingSoap11Action,
                        _options.MaxActionLength,
                        out hasAction);
                }
                else
                {
                    hasAction = contentTypeInfo.Action is not null;
                    actionSpan = contentTypeInfo.Action.AsSpan();
                }

                preparedCall = SoapEnvelopeReader.ParseAndPrepare(
                    requestUtf8.Span,
                    contentTypeInfo.Version,
                    hasAction,
                    actionSpan,
                    _operations,
                    _options,
                    services,
                    user);
            }
            catch (SoapProtocolException ex)
            {
                return CreateResultFromProtocolException(ex, correlationId, notUnderstoodHeaders: null, serviceInvoked: false);
            }

            try
            {
                await _headerProcessor.ProcessAsync(preparedCall.Context, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (SoapProtocolException ex)
            {
                IReadOnlyList<SoapQName>? notUnderstoodHeaders = ex.FaultKind == SoapFaultKind.MustUnderstand &&
                    contentTypeInfo.Version == SoapVersion.Soap12
                    ? _headerProcessor.GetUnprocessedMandatoryHeaders(SoapVersion.Soap12, preparedCall.Context.Headers)
                    : null;

                return CreateResultFromProtocolException(ex, correlationId, notUnderstoodHeaders, serviceInvoked: false);
            }
            catch (SoapFaultException ex)
            {
                SoapFault structuredFault = ex.ToSoapFault(contentTypeInfo.Version, correlationId);
                int statusCode = (int?)ex.HttpStatusCode ?? GetFaultStatusCode(contentTypeInfo.Version, ex.FaultKind);

                return StageFaultResult(
                    statusCode,
                    structuredFault,
                    notUnderstoodHeaders: null,
                    serviceInvoked: false);
            }
            catch (Exception ex)
            {
                SoapFault safeFault = SoapFaultWriter.CreateSafeFaultForException(
                    contentTypeInfo.Version,
                    ex,
                    correlationId);

                return StageFaultResult(
                    (int)HttpStatusCode.InternalServerError,
                    safeFault,
                    notUnderstoodHeaders: null,
                    serviceInvoked: false,
                    unhandledException: ex,
                    failureStage: "header_processing");
            }

            string responseContentType = contentTypeInfo.Version == SoapVersion.Soap11
                ? SoapConstants.Soap11ResponseContentType
                : SoapConstants.Soap12ResponseContentType;

            if (directResponseWriter is not null &&
                startResponseAsync is not null &&
                preparedCall.SupportsSplitExecution)
            {
                bool responseStarted = false;
                try
                {
                    await preparedCall.ExecuteAsync(cancellationToken).ConfigureAwait(false);

                    responseStarted = true;
                    await startResponseAsync(startResponseState, responseContentType, cancellationToken).ConfigureAwait(false);
                    long directBytes = preparedCall.WriteDirectResponse(directResponseWriter);

                    FlushResult flushResult = await directResponseWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
                    if (flushResult.IsCanceled)
                    {
                        throw new OperationCanceledException(cancellationToken);
                    }

                    return new SoapProcessingResult(
                        (int)HttpStatusCode.OK,
                        responseContentType,
                        ReadOnlyMemory<byte>.Empty,
                        stagedWriter: null,
                        serviceInvoked: true,
                        faultKind: null,
                        isDirectStreamed: true,
                        directStreamedBytes: directBytes);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception) when (responseStarted)
                {
                    throw;
                }
                catch (SoapProtocolException ex)
                {
                    return CreateResultFromProtocolException(
                        ex,
                        correlationId,
                        notUnderstoodHeaders: null,
                        serviceInvoked: true);
                }
                catch (SoapFaultException ex)
                {
                    SoapFault structuredFault = ex.ToSoapFault(contentTypeInfo.Version, correlationId);
                    int statusCode = (int?)ex.HttpStatusCode ?? GetFaultStatusCode(contentTypeInfo.Version, ex.FaultKind);

                    return StageFaultResult(
                        statusCode,
                        structuredFault,
                        notUnderstoodHeaders: null,
                        serviceInvoked: true);
                }
                catch (Exception ex)
                {
                    SoapFault safeFault = SoapFaultWriter.CreateSafeFaultForException(
                        contentTypeInfo.Version,
                        ex,
                        correlationId);

                    return StageFaultResult(
                        (int)HttpStatusCode.InternalServerError,
                        safeFault,
                        notUnderstoodHeaders: null,
                        serviceInvoked: true,
                        unhandledException: ex,
                        failureStage: "handler");
                }
            }

            BoundedPooledBufferWriter responseWriter = new BoundedPooledBufferWriter(
                4096,
                _options.MaxResponseBytes,
                _options.ClearPooledBuffers,
                _pool);
            bool handlerCompleted = false;

            try
            {
                if (preparedCall.SupportsSplitExecution)
                {
                    await preparedCall.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                    handlerCompleted = true;
                    _ = preparedCall.WriteDirectResponse(responseWriter);
                }
                else
                {
                    await preparedCall.ExecuteAndWriteAsync(responseWriter, cancellationToken).ConfigureAwait(false);
                    handlerCompleted = true;
                }

                SoapProcessingResult successResult = new SoapProcessingResult(
                    (int)HttpStatusCode.OK,
                    responseContentType,
                    ReadOnlyMemory<byte>.Empty,
                    responseWriter,
                    serviceInvoked: true,
                    faultKind: null);

                responseWriter = null!;

                return successResult;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (SoapProtocolException ex)
            {
                return CreateResultFromProtocolException(
                    ex,
                    correlationId,
                    notUnderstoodHeaders: null,
                    serviceInvoked: true);
            }
            catch (SoapFaultException ex)
            {
                SoapFault structuredFault = ex.ToSoapFault(contentTypeInfo.Version, correlationId);
                int statusCode = (int?)ex.HttpStatusCode ?? GetFaultStatusCode(contentTypeInfo.Version, ex.FaultKind);

                return StageFaultResult(
                    statusCode,
                    structuredFault,
                    notUnderstoodHeaders: null,
                    serviceInvoked: true);
            }
            catch (Exception ex)
            {
                SoapFault safeFault = SoapFaultWriter.CreateSafeFaultForException(
                    contentTypeInfo.Version,
                    ex,
                    correlationId);

                return StageFaultResult(
                    (int)HttpStatusCode.InternalServerError,
                    safeFault,
                    notUnderstoodHeaders: null,
                    serviceInvoked: true,
                    unhandledException: ex,
                    failureStage: handlerCompleted ? "response_serialization" : "handler");
            }
            finally
            {
                responseWriter?.Dispose();
            }
        }

        private static SoapProcessingResult CreateHttpOnlyError(HttpStatusCode statusCode, bool serviceInvoked)
        {
            return new SoapProcessingResult(
                (int)statusCode,
                contentType: null,
                ReadOnlyMemory<byte>.Empty,
                stagedWriter: null,
                serviceInvoked,
                faultKind: null);
        }

        private SoapProcessingResult CreateResultFromProtocolException(
            SoapProtocolException exception,
            string? correlationId,
            IReadOnlyList<SoapQName>? notUnderstoodHeaders,
            bool serviceInvoked)
        {
            if (exception.SoapVersion is not SoapVersion faultVersion)
            {
                return CreateHttpOnlyError(exception.HttpStatusCode, serviceInvoked);
            }

            SoapFault fault = SoapFault.CreateStandard(
                faultVersion,
                exception.FaultKind,
                exception.PublicReason,
                correlationId: correlationId);

            return StageFaultResult(
                (int)exception.HttpStatusCode,
                fault,
                notUnderstoodHeaders,
                serviceInvoked);
        }

        private SoapProcessingResult StageFaultResult(
            int statusCode,
            SoapFault fault,
            IReadOnlyList<SoapQName>? notUnderstoodHeaders,
            bool serviceInvoked,
            Exception? unhandledException = null,
            string? failureStage = null)
        {
            long desiredFaultLimit = fault.DetailXmlUtf8.IsEmpty
                ? SoapConstants.DefaultFaultBytesLimit
                : Math.Max(SoapConstants.DefaultFaultBytesLimit, (long)fault.DetailXmlUtf8.Length + 2048L);
            int faultLimit = (int)Math.Min(desiredFaultLimit, _options.MaxResponseBytes);

            BoundedPooledBufferWriter faultWriter = new BoundedPooledBufferWriter(
                512,
                faultLimit,
                _options.ClearPooledBuffers,
                _pool);

            try
            {
                SoapFaultWriter.WriteFault(fault, faultWriter, notUnderstoodHeaders);

                string faultContentType = fault.Version == SoapVersion.Soap11
                    ? SoapConstants.Soap11ResponseContentType
                    : SoapConstants.Soap12ResponseContentType;

                SoapProcessingResult result = new SoapProcessingResult(
                    statusCode,
                    faultContentType,
                    ReadOnlyMemory<byte>.Empty,
                    faultWriter,
                    serviceInvoked,
                    fault.Kind,
                    unhandledException,
                    failureStage);

                faultWriter = null!;

                return result;
            }
            catch (Exception stagingEx)
            {
                return new SoapProcessingResult(
                    (int)HttpStatusCode.InternalServerError,
                    contentType: null,
                    ReadOnlyMemory<byte>.Empty,
                    stagedWriter: null,
                    serviceInvoked,
                    fault.Kind,
                    unhandledException: unhandledException ?? stagingEx,
                    failureStage: failureStage ?? "fault_staging");
            }
            finally
            {
                faultWriter?.Dispose();
            }
        }

        #endregion

    }

}
