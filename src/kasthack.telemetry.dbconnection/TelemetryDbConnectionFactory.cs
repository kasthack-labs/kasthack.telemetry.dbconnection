using System.Data.Common;

namespace kasthack.telemetry.dbconnection;

/// <summary>
/// Creates <see cref="TelemetryDbConnection"/> wrappers that add traces and metrics to any <see cref="DbConnection"/>.
/// </summary>
public sealed class TelemetryDbConnectionFactory
{
    /// <summary>
    /// Initializes a new instance of <see cref="TelemetryDbConnectionFactory"/> with the supplied options.
    /// </summary>
    /// <param name="options">
    /// Telemetry options. When <see langword="null"/> a default instance with all options enabled is used.
    /// </param>
    public TelemetryDbConnectionFactory(TelemetryDbConnectionOptions? options = null)
    {
        Options = options ?? new TelemetryDbConnectionOptions();
    }

    /// <summary>Gets the options that control how telemetry is emitted.</summary>
    public TelemetryDbConnectionOptions Options { get; }

    /// <summary>
    /// Wraps <paramref name="connection"/> in a <see cref="TelemetryDbConnection"/> that emits traces and metrics
    /// according to <see cref="Options"/>.
    /// </summary>
    /// <param name="connection">The underlying database connection to wrap.</param>
    /// <returns>A <see cref="TelemetryDbConnection"/> that delegates all operations to <paramref name="connection"/>.</returns>
    public TelemetryDbConnection Wrap(DbConnection connection) => new(connection, this);
}
