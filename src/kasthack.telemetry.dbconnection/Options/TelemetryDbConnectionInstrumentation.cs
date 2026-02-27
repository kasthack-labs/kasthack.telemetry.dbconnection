using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace kasthack.telemetry.dbconnection.Options;

/// <summary>
/// Provides the <see cref="System.Diagnostics.ActivitySource"/> and <see cref="System.Diagnostics.Metrics.Meter"/>
/// used by this library so that consumers can register them with their telemetry pipelines.
/// </summary>
public static class TelemetryDbConnectionInstrumentation
{
    /// <summary>The name of the <see cref="System.Diagnostics.ActivitySource"/> emitted by this library.</summary>
    public const string ActivitySourceName = "kasthack.telemetry.dbconnection";

    /// <summary>The name of the <see cref="System.Diagnostics.Metrics.Meter"/> emitted by this library.</summary>
    public const string MeterName = "kasthack.telemetry.dbconnection";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    internal static readonly Meter Meter = new(MeterName);

    /// <summary>
    /// Histogram that records the duration of database client operations in seconds.
    /// Instrument name follows the OpenTelemetry semantic conventions (<c>db.client.operation.duration</c>).
    /// </summary>
    internal static readonly Histogram<double> OperationDuration = Meter.CreateHistogram<double>(
        "db.client.operation.duration",
        unit: "s",
        description: "Duration of database client operations.");
}
