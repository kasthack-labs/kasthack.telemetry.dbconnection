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
    public override void ChangeDatabase(string databaseName) =>
        ExecuteInstrumented("change_database", () => _inner.ChangeDatabase(databaseName));

    /// <inheritdoc/>
    public override Task ChangeDatabaseAsync(string databaseName, CancellationToken cancellationToken = default) =>
        ExecuteInstrumentedAsync("change_database", () => _inner.ChangeDatabaseAsync(databaseName, cancellationToken));

    /// <inheritdoc/>
    public override void Close()
    {
        if (_options.TrackConnectionManagement.HasFlag(ConnectionManagementTracking.Close))
        {
            ExecuteInstrumented("close", _inner.Close);
        }
        else
        {
            _inner.Close();
        }
    }

    /// <inheritdoc/>
    public override Task CloseAsync()
    {
        if (_options.TrackConnectionManagement.HasFlag(ConnectionManagementTracking.Close))
        {
            return ExecuteInstrumentedAsync("close", _inner.CloseAsync);
        }
        return _inner.CloseAsync();
    }

    /// <inheritdoc/>
    public override void Open()
    {
        if (_options.TrackConnectionManagement.HasFlag(ConnectionManagementTracking.Open))
        {
            ExecuteInstrumented("connect", _inner.Open);
        }
        else
        {
            _inner.Open();
        }
    }

    /// <inheritdoc/>
    public override Task OpenAsync(CancellationToken cancellationToken)
    {
        if (_options.TrackConnectionManagement.HasFlag(ConnectionManagementTracking.Open))
        {
            return ExecuteInstrumentedAsync("connect", () => _inner.OpenAsync(cancellationToken));
        }
        return _inner.OpenAsync(cancellationToken);
    }

    /// <inheritdoc/>
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
        new TelemetryDbTransaction(
            ExecuteInstrumented<DbTransaction>("begin_transaction", () => _inner.BeginTransaction(isolationLevel)),
            this);

    /// <inheritdoc/>
    protected override async ValueTask<DbTransaction> BeginDbTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken) =>
        new TelemetryDbTransaction(
            await ExecuteInstrumentedAsync<DbTransaction>("begin_transaction", async () => await _inner.BeginTransactionAsync(isolationLevel, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false),
            this);

    /// <inheritdoc/>
    protected override DbCommand CreateDbCommand()
    {
        var cmd = _inner.CreateCommand();
        return new TelemetryDbCommand(cmd, this);
    }

    /// <inheritdoc/>
    protected override DbBatch CreateDbBatch()
    {
        var batch = _inner.CreateBatch();
        return new TelemetryDbBatch(batch, this);
    }

    /// <inheritdoc/>
    public override DataTable GetSchema() =>
        ExecuteInstrumented<DataTable>("get_schema", _inner.GetSchema);

    /// <inheritdoc/>
    public override DataTable GetSchema(string collectionName) =>
        ExecuteInstrumented<DataTable>("get_schema", () => _inner.GetSchema(collectionName));

    /// <inheritdoc/>
    public override DataTable GetSchema(string collectionName, string?[] restrictionValues) =>
        ExecuteInstrumented<DataTable>("get_schema", () => _inner.GetSchema(collectionName, restrictionValues));

    /// <inheritdoc/>
    public override Task<DataTable> GetSchemaAsync(CancellationToken cancellationToken = default) =>
        ExecuteInstrumentedAsync<DataTable>("get_schema", () => _inner.GetSchemaAsync(cancellationToken));

    /// <inheritdoc/>
    public override Task<DataTable> GetSchemaAsync(string collectionName, CancellationToken cancellationToken = default) =>
        ExecuteInstrumentedAsync<DataTable>("get_schema", () => _inner.GetSchemaAsync(collectionName, cancellationToken));

    /// <inheritdoc/>
    public override Task<DataTable> GetSchemaAsync(string collectionName, string?[] restrictionValues, CancellationToken cancellationToken = default) =>
        ExecuteInstrumentedAsync<DataTable>("get_schema", () => _inner.GetSchemaAsync(collectionName, restrictionValues, cancellationToken));

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

    internal Activity? StartActivity(string operationName, string? dbStatement, DbCommand? command = null)
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

        if (!string.IsNullOrEmpty(DataSource))
        {
            activity.SetTag(DbSemanticConventions.ServerAddress, DataSource);
        }

        if (dbStatement is not null && _options.CaptureStatements)
        {
            activity.SetTag(DbSemanticConventions.DbStatement, dbStatement);
        }

        try
        {
            _options.EnrichActivity?.Invoke(activity, command);
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

    internal void RecordDuration(long startTimestamp, string operationName, string? dbStatement, DbCommand? command, bool hadError)
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

        if (!string.IsNullOrEmpty(DataSource))
        {
            tags.Add(new(DbSemanticConventions.ServerAddress, DataSource));
        }

        if (dbStatement is not null && _options.CaptureStatements)
        {
            tags.Add(new(DbSemanticConventions.DbStatement, dbStatement));
        }

        if (hadError)
        {
            tags.Add(new(DbSemanticConventions.ErrorType, "exception"));
        }

        try
        {
            _options.EnrichMetrics?.Invoke(tags, command);
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

    /// <summary>Derives the OTel <c>db.operation</c> name from a command's text.</summary>
    internal static string GetOperationName(DbCommand? command)
    {
        if (command is null)
        {
            return "connect";
        }

        var text = command.CommandText?.TrimStart();
        if (string.IsNullOrEmpty(text))
        {
            return "execute";
        }

        var spaceIndex = text.IndexOfAny([' ', '\t', '\r', '\n']);
        return spaceIndex < 0 ? text.ToUpperInvariant() : text[..spaceIndex].ToUpperInvariant();
    }

    // ── DRY execution wrappers ───────────────────────────────────────────────

    internal void ExecuteInstrumented(DbCommand? command, Action action) =>
        ExecuteInstrumentedCore<int>(GetOperationName(command), command?.CommandText, command, () => { action(); return 0; });

    internal T ExecuteInstrumented<T>(DbCommand? command, Func<T> action) =>
        ExecuteInstrumentedCore<T>(GetOperationName(command), command?.CommandText, command, action);

    internal void ExecuteInstrumented(string operationName, Action action) =>
        ExecuteInstrumentedCore<int>(operationName, null, null, () => { action(); return 0; });

    internal T ExecuteInstrumented<T>(string operationName, Func<T> action) =>
        ExecuteInstrumentedCore<T>(operationName, null, null, action);

    internal Task ExecuteInstrumentedAsync(DbCommand? command, Func<Task> action) =>
        ExecuteInstrumentedAsyncCore<int>(GetOperationName(command), command?.CommandText, command, async () => { await action().ConfigureAwait(false); return 0; });

    internal Task<T> ExecuteInstrumentedAsync<T>(DbCommand? command, Func<Task<T>> action) =>
        ExecuteInstrumentedAsyncCore<T>(GetOperationName(command), command?.CommandText, command, action);

    internal Task ExecuteInstrumentedAsync(string operationName, Func<Task> action) =>
        ExecuteInstrumentedAsyncCore<int>(operationName, null, null, async () => { await action().ConfigureAwait(false); return 0; });

    internal Task<T> ExecuteInstrumentedAsync<T>(string operationName, Func<Task<T>> action) =>
        ExecuteInstrumentedAsyncCore<T>(operationName, null, null, action);

    internal DbDataReader ExecuteInstrumentedReader(DbCommand command, Func<DbDataReader> execute)
    {
        var operationName = GetOperationName(command);
        var dbStatement = command.CommandText;
        var activity = StartActivity(operationName, dbStatement, command);
        var startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            return new TelemetryDbDataReader(execute(), this, activity, startTimestamp, operationName, dbStatement);
        }
        catch (Exception ex)
        {
            SetActivityError(activity, ex);
            RecordDuration(startTimestamp, operationName, dbStatement, command, hadError: true);
            activity?.Dispose();
            throw;
        }
    }

    internal async Task<DbDataReader> ExecuteInstrumentedReaderAsync(DbCommand command, Func<Task<DbDataReader>> execute)
    {
        var operationName = GetOperationName(command);
        var dbStatement = command.CommandText;
        var activity = StartActivity(operationName, dbStatement, command);
        var startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            return new TelemetryDbDataReader(await execute().ConfigureAwait(false), this, activity, startTimestamp, operationName, dbStatement);
        }
        catch (Exception ex)
        {
            SetActivityError(activity, ex);
            RecordDuration(startTimestamp, operationName, dbStatement, command, hadError: true);
            activity?.Dispose();
            throw;
        }
    }

    internal DbDataReader ExecuteInstrumentedReader(string operationName, Func<DbDataReader> execute)
    {
        var activity = StartActivity(operationName, null, null);
        var startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            return new TelemetryDbDataReader(execute(), this, activity, startTimestamp, operationName, null);
        }
        catch (Exception ex)
        {
            SetActivityError(activity, ex);
            RecordDuration(startTimestamp, operationName, null, null, hadError: true);
            activity?.Dispose();
            throw;
        }
    }

    internal async Task<DbDataReader> ExecuteInstrumentedReaderAsync(string operationName, Func<Task<DbDataReader>> execute)
    {
        var activity = StartActivity(operationName, null, null);
        var startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            return new TelemetryDbDataReader(await execute().ConfigureAwait(false), this, activity, startTimestamp, operationName, null);
        }
        catch (Exception ex)
        {
            SetActivityError(activity, ex);
            RecordDuration(startTimestamp, operationName, null, null, hadError: true);
            activity?.Dispose();
            throw;
        }
    }

    private T ExecuteInstrumentedCore<T>(string operationName, string? statement, DbCommand? command, Func<T> action)
    {
        using var activity = StartActivity(operationName, statement, command);
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
            RecordDuration(start, operationName, statement, command, hadError);
        }
    }

    private async Task<T> ExecuteInstrumentedAsyncCore<T>(string operationName, string? statement, DbCommand? command, Func<Task<T>> action)
    {
        using var activity = StartActivity(operationName, statement, command);
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
            RecordDuration(start, operationName, statement, command, hadError);
        }
    }
}
