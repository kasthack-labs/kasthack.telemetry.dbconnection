using System.Data.Common;
using System.Diagnostics;

namespace kasthack.telemetry.dbconnection;

/// <summary>
/// Options that control how <see cref="TelemetryDbConnectionFactory"/> emits telemetry.
/// </summary>
public sealed class TelemetryDbConnectionOptions
{
    /// <summary>Gets or sets a value indicating whether distributed traces (Activities) are emitted. Default is <see langword="true"/>.</summary>
    public bool EmitTraces { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether metrics (histograms) are emitted. Default is <see langword="true"/>.</summary>
    public bool EmitMetrics { get; set; } = true;

    /// <summary>
    /// Gets or sets an optional callback that is invoked after an <see cref="Activity"/> has been created and
    /// its default tags have been populated, allowing callers to add custom tags or otherwise enrich the activity.
    /// </summary>
    public Action<Activity, DbConnection>? EnrichActivity { get; set; }

    /// <summary>
    /// Gets or sets an optional callback that is invoked before a metric measurement is recorded.
    /// The callback receives a mutable list of tags and the underlying <see cref="DbConnection"/>;
    /// any items added to the list will be included in the recorded measurement.
    /// </summary>
    public Action<IList<KeyValuePair<string, object?>>, DbConnection>? EnrichMetrics { get; set; }
}
