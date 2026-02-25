using System.Data.Common;
using Microsoft.Extensions.Logging;

namespace kasthack.telemetry.dbconnection;

/// <summary>
/// Creates <see cref="TelemetryDbConnection"/> wrappers that add traces and metrics to any <see cref="DbConnection"/>.
/// </summary>
public sealed class TelemetryDbConnectionFactory(
    TelemetryDbConnectionOptions? options = null,
    ILogger<TelemetryDbConnectionFactory>? logger = null)
{
    /// <summary>Gets the options that control how telemetry is emitted.</summary>
    public TelemetryDbConnectionOptions Options { get; } = options ?? new TelemetryDbConnectionOptions();

    /// <summary>
    /// Wraps <paramref name="connection"/> in a <see cref="TelemetryDbConnection"/> that emits traces and metrics
    /// according to <see cref="Options"/>.
    /// </summary>
    /// <param name="connection">The underlying database connection to wrap.</param>
    /// <returns>A <see cref="TelemetryDbConnection"/> that delegates all operations to <paramref name="connection"/>.</returns>
    public TelemetryDbConnection Wrap(DbConnection connection) => new(connection, Options, logger);
}
