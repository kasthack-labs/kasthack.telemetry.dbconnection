using System.Data.Common;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using kasthack.telemetry.dbconnection;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace kasthack.telemetry.dbconnection.ef;

/// <summary>
/// An Entity Framework Core <see cref="DbConnectionInterceptor"/> that emits OpenTelemetry-compatible
/// traces and metrics for connection lifecycle events (open/close) using the
/// <see cref="TelemetryDbConnectionInstrumentation"/> activity source and meter.
/// </summary>
/// <remarks>
/// Register this interceptor via <c>DbContextOptionsBuilder.AddInterceptors</c>.
/// </remarks>
public sealed class TelemetryDbConnectionInterceptor : DbConnectionInterceptor
{
    private readonly TelemetryDbConnectionOptions _options;

    // Associates an in-flight Activity with the DbConnection that triggered it.
    // ConditionalWeakTable uses weak references for keys so it does not prevent GC.
    private readonly ConditionalWeakTable<DbConnection, Activity> _pending = new();

    /// <summary>
    /// Initializes a new instance of <see cref="TelemetryDbConnectionInterceptor"/>.
    /// </summary>
    /// <param name="options">Telemetry options. When <see langword="null"/> a default instance is used.</param>
    public TelemetryDbConnectionInterceptor(TelemetryDbConnectionOptions? options = null)
    {
        _options = options ?? new TelemetryDbConnectionOptions();
    }

    // ── Opening ──────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public override InterceptionResult ConnectionOpening(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        ArgumentNullException.ThrowIfNull(connection);
        StartActivity(connection);
        return base.ConnectionOpening(connection, eventData, result);
    }

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        StartActivity(connection);
        return base.ConnectionOpeningAsync(connection, eventData, result, cancellationToken);
    }

    // ── Opened ───────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(eventData);
        FinishActivity(connection, eventData.Duration, hadError: false);
        base.ConnectionOpened(connection, eventData);
    }

    /// <inheritdoc/>
    public override Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(eventData);
        FinishActivity(connection, eventData.Duration, hadError: false);
        return base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    // ── Failed ───────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public override void ConnectionFailed(DbConnection connection, ConnectionErrorEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(eventData);
        FinishActivityWithError(connection, eventData.Duration, eventData.Exception);
        base.ConnectionFailed(connection, eventData);
    }

    /// <inheritdoc/>
    public override Task ConnectionFailedAsync(
        DbConnection connection,
        ConnectionErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(eventData);
        FinishActivityWithError(connection, eventData.Duration, eventData.Exception);
        return base.ConnectionFailedAsync(connection, eventData, cancellationToken);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private void StartActivity(DbConnection connection)
    {
        if (!_options.EmitTraces)
        {
            return;
        }

        var activity = TelemetryDbConnectionInstrumentation.ActivitySource.StartActivity(
            DbSemanticConventions.ConnectOperation,
            ActivityKind.Client);

        if (activity is null)
        {
            return;
        }

        activity.SetTag(DbSemanticConventions.DbSystem, GetDbSystem(connection));
        activity.SetTag(DbSemanticConventions.DbName, connection.Database);
        activity.SetTag(DbSemanticConventions.DbOperation, DbSemanticConventions.ConnectOperation);
        _options.EnrichActivity?.Invoke(activity, connection);

        _pending.AddOrUpdate(connection, activity);
    }

    private void FinishActivity(DbConnection connection, TimeSpan duration, bool hadError)
    {
        if (_pending.TryGetValue(connection, out var activity))
        {
            _pending.Remove(connection);
            activity.Dispose();
        }

        RecordMetric(connection, duration, hadError);
    }

    private void FinishActivityWithError(DbConnection connection, TimeSpan duration, Exception? exception)
    {
        if (_pending.TryGetValue(connection, out var activity))
        {
            _pending.Remove(connection);
            if (exception is not null)
            {
                activity.SetStatus(ActivityStatusCode.Error, exception.Message);
                activity.SetTag(DbSemanticConventions.ErrorType, exception.GetType().FullName);
            }

            activity.Dispose();
        }

        RecordMetric(connection, duration, hadError: exception is not null);
    }

    private void RecordMetric(DbConnection connection, TimeSpan duration, bool hadError)
    {
        if (!_options.EmitMetrics)
        {
            return;
        }

        var tags = new List<KeyValuePair<string, object?>>
        {
            new(DbSemanticConventions.DbSystem, GetDbSystem(connection)),
            new(DbSemanticConventions.DbName, connection.Database),
            new(DbSemanticConventions.DbOperation, DbSemanticConventions.ConnectOperation),
        };

        if (hadError)
        {
            tags.Add(new(DbSemanticConventions.ErrorType, "exception"));
        }

        _options.EnrichMetrics?.Invoke(tags, connection);

        var tagList = new TagList();
        foreach (var tag in tags)
        {
            tagList.Add(tag);
        }

        TelemetryDbConnectionInstrumentation.OperationDuration.Record(duration.TotalSeconds, tagList);
    }

    private static string GetDbSystem(DbConnection connection)
    {
        var typeName = connection.GetType().Name;
        return typeName switch
        {
            var t when t.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) => "sqlite",
            var t when t.Contains("MySql", StringComparison.OrdinalIgnoreCase) => "mysql",
            var t when t.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) || t.Contains("Postgres", StringComparison.OrdinalIgnoreCase) => "postgresql",
            var t when t.Contains("SqlClient", StringComparison.OrdinalIgnoreCase) || t.Contains("Mssql", StringComparison.OrdinalIgnoreCase) || t.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) => "mssql",
            _ => "other_sql",
        };
    }
}

