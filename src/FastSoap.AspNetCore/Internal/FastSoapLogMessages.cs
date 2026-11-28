using System;

using Microsoft.Extensions.Logging;

namespace FastSoap.AspNetCore.Internal
{

    /// <summary>
    /// Source-generated structured log messages for FastSoap ASP.NET Core hosting with zero payload logging.
    /// </summary>
    internal static partial class FastSoapLogMessages
    {

        #region Internal Methods

        [LoggerMessage(
            EventId = 1001,
            Level = LogLevel.Information,
            Message = "Mapped FastSoap endpoint '{EndpointPattern}' with {OperationCount} operations (Versions={EnabledVersions}, MetadataEnabled={MetadataEnabled}).")]
        internal static partial void EndpointMapped(
            ILogger logger,
            string endpointPattern,
            int operationCount,
            string enabledVersions,
            bool metadataEnabled);

        [LoggerMessage(
            EventId = 1005,
            Level = LogLevel.Error,
            Message = "Unhandled exception while processing SOAP operation '{OperationName}' on endpoint '{EndpointPattern}' (Version={SoapVersion}).")]
        internal static partial void OperationFailed(
            ILogger logger,
            string endpointPattern,
            string operationName,
            string soapVersion,
            Exception exception);

        [LoggerMessage(
            EventId = 1003,
            Level = LogLevel.Warning,
            Message = "Rejected SOAP request on endpoint '{EndpointPattern}' (Version={SoapVersion}, StatusCode={StatusCode}, FaultKind={FaultKind}, Reason={PublicReason}).")]
        internal static partial void ProtocolRejection(
            ILogger logger,
            string endpointPattern,
            string soapVersion,
            int statusCode,
            string faultKind,
            string publicReason);

        [LoggerMessage(
            EventId = 1004,
            Level = LogLevel.Warning,
            Message = "Rejected oversized SOAP request on endpoint '{EndpointPattern}' exceeding {MaxRequestBytes} bytes.")]
        internal static partial void QuotaRejected(
            ILogger logger,
            string endpointPattern,
            int maxRequestBytes);

        [LoggerMessage(
            EventId = 1006,
            Level = LogLevel.Debug,
            Message = "SOAP request aborted on endpoint '{EndpointPattern}'.")]
        internal static partial void RequestAborted(
            ILogger logger,
            string endpointPattern);

        [LoggerMessage(
            EventId = 1002,
            Level = LogLevel.Debug,
            Message = "Completed SOAP request on endpoint '{EndpointPattern}' (Operation={OperationName}, Version={SoapVersion}, StatusCode={StatusCode}, Outcome={Outcome}).")]
        internal static partial void RequestCompleted(
            ILogger logger,
            string endpointPattern,
            string operationName,
            string soapVersion,
            int statusCode,
            string outcome);

        [LoggerMessage(
            EventId = 1007,
            Level = LogLevel.Error,
            Message = "Failed committing SOAP response after headers started on endpoint '{EndpointPattern}'; aborting connection.")]
        internal static partial void ResponseCommitFailedAfterStart(
            ILogger logger,
            string endpointPattern,
            Exception exception);

        [LoggerMessage(
            EventId = 1008,
            Level = LogLevel.Error,
            Message = "Unhandled exception staged as sanitized SOAP fault on endpoint '{EndpointPattern}' (Operation={OperationName}, Version={SoapVersion}, StatusCode={StatusCode}, FaultKind={FaultKind}, FailureStage={FailureStage}).")]
        internal static partial void UnhandledProcessingFault(
            ILogger logger,
            string endpointPattern,
            string operationName,
            string soapVersion,
            int statusCode,
            string faultKind,
            string failureStage,
            Exception exception);

        #endregion

    }

}
