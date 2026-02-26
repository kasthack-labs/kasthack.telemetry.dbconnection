using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

#pragma warning disable CA2100 // CommandText is a pass-through; SQL review is the caller's responsibility

namespace kasthack.telemetry.dbconnection;

/// <summary>
/// A <see cref="DbCommand"/> decorator that measures the execution time of every command operation and
/// emits traces and metrics via the owning <see cref="TelemetryDbConnection"/>.
/// The same command instance may be reused; each execution produces its own telemetry.
/// </summary>
public sealed class TelemetryDbCommand : DbCommand
{
    private const string CancelOperation = "cancel";
    private const string PrepareOperation = "prepare";

    private readonly DbCommand _inner;
    private readonly TelemetryDbConnection _connection;

    internal TelemetryDbCommand(DbCommand command, TelemetryDbConnection connection)
    {
        _inner = command;
        _connection = connection;
    }

    /// <inheritdoc/>
    [AllowNull]
    public override string CommandText
    {
        get => _inner.CommandText;
        set => _inner.CommandText = value;
    }

    /// <inheritdoc/>
    public override int CommandTimeout
    {
        get => _inner.CommandTimeout;
        set => _inner.CommandTimeout = value;
    }

    /// <inheritdoc/>
    public override CommandType CommandType
    {
        get => _inner.CommandType;
        set => _inner.CommandType = value;
    }

    /// <inheritdoc/>
    public override bool DesignTimeVisible
    {
        get => _inner.DesignTimeVisible;
        set => _inner.DesignTimeVisible = value;
    }

    /// <inheritdoc/>
    public override UpdateRowSource UpdatedRowSource
    {
        get => _inner.UpdatedRowSource;
        set => _inner.UpdatedRowSource = value;
    }

    /// <inheritdoc/>
    protected override DbConnection? DbConnection
    {
        get => _connection;
        set
        {
            // Unwrap TelemetryDbConnection to set inner; not used in normal EF/ADO flows.
            _inner.Connection = value is TelemetryDbConnection telemetry
                ? telemetry.InnerConnection
                : value;
        }
    }

    /// <inheritdoc/>
    protected override DbParameterCollection DbParameterCollection => _inner.Parameters;

    /// <inheritdoc/>
    protected override DbTransaction? DbTransaction
    {
        get => _inner.Transaction;
        set => _inner.Transaction = value is TelemetryDbTransaction t ? t.InnerTransaction : value;
    }

    /// <inheritdoc/>
    public override void Cancel() => _connection.ExecuteInstrumented(CancelOperation, _inner.Cancel);

    /// <inheritdoc/>
    public override void Prepare() => _connection.ExecuteInstrumented(PrepareOperation, _inner.Prepare);

    /// <inheritdoc/>
    public override Task PrepareAsync(CancellationToken cancellationToken = default) =>
        _connection.ExecuteInstrumentedAsync(PrepareOperation, () => _inner.PrepareAsync(cancellationToken));

    /// <inheritdoc/>
    protected override DbParameter CreateDbParameter() => _inner.CreateParameter();

    // ── Synchronous execute ──────────────────────────────────────────────────

    /// <inheritdoc/>
    public override int ExecuteNonQuery() =>
        _connection.ExecuteInstrumented(_inner, _inner.ExecuteNonQuery);

    /// <inheritdoc/>
    public override object? ExecuteScalar() =>
        _connection.ExecuteInstrumented<object?>(_inner, _inner.ExecuteScalar);

    /// <inheritdoc/>
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
        _connection.ExecuteInstrumentedReader(_inner, () => _inner.ExecuteReader(behavior));

    // ── Asynchronous execute ─────────────────────────────────────────────────

    /// <inheritdoc/>
    public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken) =>
        _connection.ExecuteInstrumentedAsync(_inner, () => _inner.ExecuteNonQueryAsync(cancellationToken));

    /// <inheritdoc/>
    public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken) =>
        _connection.ExecuteInstrumentedAsync<object?>(_inner, () => _inner.ExecuteScalarAsync(cancellationToken));

    /// <inheritdoc/>
    protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken) =>
        _connection.ExecuteInstrumentedReaderAsync(_inner, () => _inner.ExecuteReaderAsync(behavior, cancellationToken));

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
