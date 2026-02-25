using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace kasthack.telemetry.dbconnection;

/// <summary>
/// A <see cref="DbConnection"/> decorator that measures operation timings and emits
/// OpenTelemetry-compatible traces and metrics for every operation performed through it.
/// </summary>
public sealed class TelemetryDbConnection : DbConnection
{
    private static readonly Action<ILogger, Exception?> _logEnrichActivityError =
        LoggerMessage.Define(LogLevel.Warning, new EventId(1, "EnrichActivityError"), "An exception occurred in the EnrichActivity callback.");

    private static readonly Action<ILogger, Exception?> _logEnrichMetricsError =
        LoggerMessage.Define(LogLevel.Warning, new EventId(2, "EnrichMetricsError"), "An exception occurred in the EnrichMetrics callback.");

    private readonly DbConnection _inner;
    private readonly TelemetryDbConnectionOptions _options;
    private readonly ILogger? _logger;
    private readonly StateChangeEventHandler _stateChangeHandler;

    /// <summary>
    /// Initializes a new instance of <see cref="TelemetryDbConnection"/>.
    /// </summary>
    /// <param name="connection">The underlying database connection to wrap.</param>
    /// <param name="options">Telemetry options that control how traces and metrics are emitted.</param>
    /// <param name="logger">Optional logger used to report errors from enrichment callbacks.</param>
    public TelemetryDbConnection(DbConnection connection, TelemetryDbConnectionOptions options, ILogger? logger = null)
    {
        _inner = connection;
        _options = options;
        _logger = logger;
        _stateChangeHandler = (_, e) => OnStateChange(e);
        _inner.StateChange += _stateChangeHandler;
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
    public override void Open() => ExecuteInstrumented("connect", null, _inner.Open);

    /// <inheritdoc/>
    public override Task OpenAsync(CancellationToken cancellationToken) =>
        ExecuteInstrumentedAsync("connect", null, () => _inner.OpenAsync(cancellationToken));

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
            _inner.StateChange -= _stateChangeHandler;
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    // ── Internal helpers used by TelemetryDbCommand ─────────────────────────

    internal Activity? StartActivity(string operationName, string? dbStatement)
    {
        if (!_options.EmitTraces)
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

        activity.SetTag(DbSemanticConventions.DbName, Database);
        activity.SetTag(DbSemanticConventions.DbOperation, operationName);

        if (dbStatement is not null)
        {
            activity.SetTag(DbSemanticConventions.DbStatement, dbStatement);
        }

        try
        {
            _options.EnrichActivity?.Invoke(activity, _inner);
        }
#pragma warning disable CA1031 // Catching Exception is intentional: user callbacks must never break the operation
        catch (Exception ex)
#pragma warning restore CA1031
        {
            if (_logger is not null)
            {
                _logEnrichActivityError(_logger, ex);
            }
        }

        return activity;
    }

    internal void RecordDuration(long startTimestamp, string operationName, string? dbStatement, bool hadError)
    {
        if (!_options.EmitMetrics)
        {
            return;
        }

        var duration = Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds;
        var tags = new List<KeyValuePair<string, object?>>
        {
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

        try
        {
            _options.EnrichMetrics?.Invoke(tags, _inner);
        }
#pragma warning disable CA1031 // Catching Exception is intentional: user callbacks must never break the operation
        catch (Exception ex)
#pragma warning restore CA1031
        {
            if (_logger is not null)
            {
                _logEnrichMetricsError(_logger, ex);
            }
        }

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

    // ── DRY execution wrappers ───────────────────────────────────────────────

    internal void ExecuteInstrumented(string operation, string? statement, Action action)
    {
        using var activity = StartActivity(operation, statement);
        var start = Stopwatch.GetTimestamp();
        var hadError = false;
        try
        {
            action();
        }
        catch (Exception ex)
        {
            hadError = true;
            SetActivityError(activity, ex);
            throw;
        }
        finally
        {
            RecordDuration(start, operation, statement, hadError);
        }
    }

    internal T ExecuteInstrumented<T>(string operation, string? statement, Func<T> action)
    {
        using var activity = StartActivity(operation, statement);
        var start = Stopwatch.GetTimestamp();
        var hadError = false;
        try
        {
            return action();
        }
        catch (Exception ex)
        {
            hadError = true;
            SetActivityError(activity, ex);
            throw;
        }
        finally
        {
            RecordDuration(start, operation, statement, hadError);
        }
    }

    internal async Task ExecuteInstrumentedAsync(string operation, string? statement, Func<Task> action)
    {
        using var activity = StartActivity(operation, statement);
        var start = Stopwatch.GetTimestamp();
        var hadError = false;
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            hadError = true;
            SetActivityError(activity, ex);
            throw;
        }
        finally
        {
            RecordDuration(start, operation, statement, hadError);
        }
    }

    internal async Task<T> ExecuteInstrumentedAsync<T>(string operation, string? statement, Func<Task<T>> action)
    {
        using var activity = StartActivity(operation, statement);
        var start = Stopwatch.GetTimestamp();
        var hadError = false;
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            hadError = true;
            SetActivityError(activity, ex);
            throw;
        }
        finally
        {
            RecordDuration(start, operation, statement, hadError);
        }
    }
}
