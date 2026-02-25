using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace kasthack.telemetry.dbconnection;

/// <summary>
/// A <see cref="DbConnection"/> decorator that measures operation timings and emits
/// OpenTelemetry-compatible traces and metrics for every operation performed through it.
/// </summary>
public sealed class TelemetryDbConnection : DbConnection
{
    private readonly DbConnection _inner;
    private readonly TelemetryDbConnectionFactory _factory;

    /// <summary>
    /// Initializes a new instance of <see cref="TelemetryDbConnection"/>.
    /// </summary>
    /// <param name="connection">The underlying database connection to wrap.</param>
    /// <param name="factory">The factory that owns this connection and supplies telemetry options.</param>
    public TelemetryDbConnection(DbConnection connection, TelemetryDbConnectionFactory factory)
    {
        _inner = connection;
        _factory = factory;
        _inner.StateChange += OnInnerStateChange;
    }

    /// <summary>Gets the underlying <see cref="DbConnection"/> that this instance wraps.</summary>
    public DbConnection InnerConnection => _inner;

    /// <inheritdoc/>
    [AllowNull]
    public override string ConnectionString
    {
        get => _inner.ConnectionString;
        set => _inner.ConnectionString = value;
    }

    /// <inheritdoc/>
    public override string Database => _inner.Database;

    /// <inheritdoc/>
    public override string DataSource => _inner.DataSource;

    /// <inheritdoc/>
    public override string ServerVersion => _inner.ServerVersion;

    /// <inheritdoc/>
    public override ConnectionState State => _inner.State;

    /// <inheritdoc/>
    public override void ChangeDatabase(string databaseName) => _inner.ChangeDatabase(databaseName);

    /// <inheritdoc/>
    public override void Close() => _inner.Close();

    /// <inheritdoc/>
    public override void Open()
    {
        using var activity = StartActivity(DbSemanticConventions.ConnectOperation, null);
        var startTimestamp = Stopwatch.GetTimestamp();
        var hadError = false;
        try
        {
            _inner.Open();
        }
        catch (Exception ex)
        {
            hadError = true;
            SetActivityError(activity, ex);
            throw;
        }
        finally
        {
            RecordDuration(startTimestamp, DbSemanticConventions.ConnectOperation, null, hadError);
        }
    }

    /// <inheritdoc/>
    public override async Task OpenAsync(CancellationToken cancellationToken)
    {
        using var activity = StartActivity(DbSemanticConventions.ConnectOperation, null);
        var startTimestamp = Stopwatch.GetTimestamp();
        var hadError = false;
        try
        {
            await _inner.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            hadError = true;
            SetActivityError(activity, ex);
            throw;
        }
        finally
        {
            RecordDuration(startTimestamp, DbSemanticConventions.ConnectOperation, null, hadError);
        }
    }

    /// <inheritdoc/>
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
        _inner.BeginTransaction(isolationLevel);

    /// <inheritdoc/>
    protected override DbCommand CreateDbCommand()
    {
        var cmd = _inner.CreateCommand();
        return new TelemetryDbCommand(cmd, this);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.StateChange -= OnInnerStateChange;
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    // ── Internal helpers used by TelemetryDbCommand ─────────────────────────

    internal Activity? StartActivity(string operationName, string? dbStatement)
    {
        if (!_factory.Options.EmitTraces)
        {
            return null;
        }

        var activity = TelemetryDbConnectionInstrumentation.ActivitySource.StartActivity(
            operationName,
            ActivityKind.Client);

        if (activity is null)
        {
            return null;
        }

        activity.SetTag(DbSemanticConventions.DbSystem, GetDbSystem());
        activity.SetTag(DbSemanticConventions.DbName, Database);
        activity.SetTag(DbSemanticConventions.DbOperation, operationName);

        if (dbStatement is not null)
        {
            activity.SetTag(DbSemanticConventions.DbStatement, dbStatement);
        }

        _factory.Options.EnrichActivity?.Invoke(activity, _inner);
        return activity;
    }

    internal void RecordDuration(long startTimestamp, string operationName, string? dbStatement, bool hadError)
    {
        if (!_factory.Options.EmitMetrics)
        {
            return;
        }

        var duration = Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds;
        var tags = new List<KeyValuePair<string, object?>>
        {
            new(DbSemanticConventions.DbSystem, GetDbSystem()),
            new(DbSemanticConventions.DbName, Database),
            new(DbSemanticConventions.DbOperation, operationName),
        };

        if (dbStatement is not null)
        {
            tags.Add(new(DbSemanticConventions.DbStatement, dbStatement));
        }

        if (hadError)
        {
            tags.Add(new(DbSemanticConventions.ErrorType, "exception"));
        }

        _factory.Options.EnrichMetrics?.Invoke(tags, _inner);

        var tagList = new TagList();
        foreach (var tag in tags)
        {
            tagList.Add(tag);
        }

        TelemetryDbConnectionInstrumentation.OperationDuration.Record(duration, tagList);
    }

    internal static void SetActivityError(Activity? activity, Exception ex)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity.SetTag(DbSemanticConventions.ErrorType, ex.GetType().FullName);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private void OnInnerStateChange(object sender, StateChangeEventArgs e) => OnStateChange(e);

    private string GetDbSystem()
    {
        var typeName = _inner.GetType().Name;
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
