using System.Data;
using System.Data.Common;

#pragma warning disable CA2100 // CommandText is a pass-through; SQL review is the caller's responsibility

namespace kasthack.telemetry.dbconnection.Decorators;

/// <summary>
/// A <see cref="DbBatch"/> decorator that measures the execution time of every batch operation and
/// emits traces and metrics via the owning <see cref="TelemetryDbConnection"/>.
/// </summary>
public sealed class TelemetryDbBatch : DbBatch
{
    private const string BatchOperation = "batch";
    private const string PrepareOperation = "prepare";
    private const string CancelOperation = "cancel";

    private readonly DbBatch _inner;
    private readonly TelemetryDbConnection _connection;

    internal TelemetryDbBatch(DbBatch batch, TelemetryDbConnection connection)
    {
        _inner = batch;
        _connection = connection;
    }

    /// <inheritdoc/>
    public override int Timeout
    {
        get => _inner.Timeout;
        set => _inner.Timeout = value;
    }

    /// <inheritdoc/>
    protected override DbBatchCommandCollection DbBatchCommands => _inner.BatchCommands;

    /// <inheritdoc/>
    protected override DbConnection? DbConnection
    {
        get => _connection;
        set => _inner.Connection = value is TelemetryDbConnection t ? t.InnerConnection : value;
    }

    /// <inheritdoc/>
    protected override DbTransaction? DbTransaction
    {
        get => _inner.Transaction;
        set => _inner.Transaction = value is TelemetryDbTransaction t ? t.InnerTransaction : value;
    }

    /// <inheritdoc/>
    public override void Cancel() => _connection.ExecuteInstrumented(CancelOperation, _inner.Cancel);

    /// <inheritdoc/>
    public override int ExecuteNonQuery() =>
        _connection.ExecuteInstrumented<int>(BatchOperation, _inner.ExecuteNonQuery);

    /// <inheritdoc/>
    public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default) =>
        _connection.ExecuteInstrumentedAsync<int>(BatchOperation, () => _inner.ExecuteNonQueryAsync(cancellationToken));

    /// <inheritdoc/>
    public override object? ExecuteScalar() =>
        _connection.ExecuteInstrumented<object?>(BatchOperation, _inner.ExecuteScalar);

    /// <inheritdoc/>
    public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken = default) =>
        _connection.ExecuteInstrumentedAsync<object?>(BatchOperation, () => _inner.ExecuteScalarAsync(cancellationToken));

    /// <inheritdoc/>
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
        _connection.ExecuteInstrumentedReader(BatchOperation, () => _inner.ExecuteReader(behavior));

    /// <inheritdoc/>
    protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken) =>
        _connection.ExecuteInstrumentedReaderAsync(BatchOperation, () => _inner.ExecuteReaderAsync(behavior, cancellationToken));

    /// <inheritdoc/>
    public override void Prepare() =>
        _connection.ExecuteInstrumented(PrepareOperation, _inner.Prepare);

    /// <inheritdoc/>
    public override Task PrepareAsync(CancellationToken cancellationToken = default) =>
        _connection.ExecuteInstrumentedAsync(PrepareOperation, () => _inner.PrepareAsync(cancellationToken));

    /// <inheritdoc/>
    protected override DbBatchCommand CreateDbBatchCommand() => _inner.CreateBatchCommand();

    /// <inheritdoc/>
    public override void Dispose()
    {
        _inner.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
