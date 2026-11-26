using System.Diagnostics;
using System.Diagnostics.Metrics;

using FastSoap.Abstractions;

namespace FastSoap.AspNetCore
{

    /// <summary>
    /// Exposes the named <see cref="System.Diagnostics.ActivitySource"/> and <see cref="System.Diagnostics.Metrics.Meter"/>
    /// instruments emitted by FastSoap ASP.NET Core endpoints using low-cardinality protocol tags.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddOpenTelemetry()
    ///     .WithTracing(tracing => tracing.AddSource(FastSoapTelemetry.ActivitySourceName))
    ///     .WithMetrics(metrics => metrics.AddMeter(FastSoapTelemetry.MeterName));
    /// </code>
    /// </example>
    /// <remarks>
    /// Tags are restricted to bounded protocol identifiers (<c>endpoint</c>, <c>operation</c>, <c>soap.version</c>,
    /// <c>fault.kind</c>, <c>outcome</c>, and <c>http.status_class</c>). Raw <c>SOAPAction</c> values, XML payloads,
    /// correlation IDs, and exception messages are never emitted as metric or trace tags.
    /// </remarks>
    public static class FastSoapTelemetry
    {

        #region Fields

        /// <summary>
        /// The canonical <see cref="System.Diagnostics.ActivitySource"/> name (<c>FastSoap</c>).
        /// </summary>
        public const string ActivitySourceName = "FastSoap";

        /// <summary>
        /// The canonical <see cref="System.Diagnostics.Metrics.Meter"/> name (<c>FastSoap</c>).
        /// </summary>
        public const string MeterName = "FastSoap";

        /// <summary>
        /// Gets the shared <see cref="System.Diagnostics.ActivitySource"/> for FastSoap request processing spans.
        /// </summary>
        public static readonly ActivitySource ActivitySource = new ActivitySource(ActivitySourceName, "0.1.0");

        /// <summary>
        /// Gets the shared <see cref="System.Diagnostics.Metrics.Meter"/> for FastSoap server metrics.
        /// </summary>
        public static readonly Meter Meter = new Meter(MeterName, "0.1.0");

        internal static readonly UpDownCounter<long> ActiveRequests = Meter.CreateUpDownCounter<long>(
            "fastsoap.server.active_requests",
            unit: "{request}",
            description: "Number of SOAP POST requests currently being processed.");

        internal static readonly Counter<long> CancellationsTotal = Meter.CreateCounter<long>(
            "fastsoap.server.cancellations",
            unit: "{cancellation}",
            description: "Total number of SOAP requests canceled due to client disconnect or abort.");

        internal static readonly Counter<long> FaultsTotal = Meter.CreateCounter<long>(
            "fastsoap.server.faults",
            unit: "{fault}",
            description: "Total number of SOAP fault responses staged or returned.");

        internal static readonly Counter<long> QuotaRejectionsTotal = Meter.CreateCounter<long>(
            "fastsoap.server.quota_rejections",
            unit: "{rejection}",
            description: "Total number of SOAP requests rejected due to request size quota enforcement.");

        internal static readonly Histogram<long> RequestBytes = Meter.CreateHistogram<long>(
            "fastsoap.server.request.bytes",
            unit: "By",
            description: "Observed bounded UTF-8 request payload size in bytes.");

        internal static readonly Histogram<double> RequestDurationMs = Meter.CreateHistogram<double>(
            "fastsoap.server.request.duration",
            unit: "ms",
            description: "End-to-end SOAP request processing duration in milliseconds.");

        internal static readonly Counter<long> RequestsTotal = Meter.CreateCounter<long>(
            "fastsoap.server.requests",
            unit: "{request}",
            description: "Total number of SOAP POST requests handled by FastSoap endpoints.");

        internal static readonly Histogram<long> ResponseBytes = Meter.CreateHistogram<long>(
            "fastsoap.server.response.bytes",
            unit: "By",
            description: "Observed staged UTF-8 response payload size in bytes.");

        #endregion

        #region Internal Methods

        internal static string FormatFaultKindTag(SoapFaultKind? faultKind)
        {
            return faultKind switch
            {
                SoapFaultKind.Sender => "sender",
                SoapFaultKind.Receiver => "receiver",
                SoapFaultKind.MustUnderstand => "must_understand",
                SoapFaultKind.VersionMismatch => "version_mismatch",
                _ => "none"
            };
        }

        internal static string FormatStatusClassTag(int statusCode)
        {
            if (statusCode >= 200 && statusCode < 300)
            {
                return "2xx";
            }

            if (statusCode >= 400 && statusCode < 500)
            {
                return "4xx";
            }

            if (statusCode >= 500 && statusCode < 600)
            {
                return "5xx";
            }

            return "other";
        }

        internal static string FormatVersionTag(SoapVersion? version)
        {
            return version switch
            {
                SoapVersion.Soap11 => "soap11",
                SoapVersion.Soap12 => "soap12",
                _ => "unknown"
            };
        }

        #endregion

    }

}
