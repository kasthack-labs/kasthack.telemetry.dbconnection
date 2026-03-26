using System.Data;
using System.Data.Common;

namespace kasthack.telemetry.dbconnection.Decorators;

/// <summary>
/// A <see cref="DbTransaction"/> decorator that reports its connection as the owning
/// <see cref="TelemetryDbConnection"/> rather than the underlying inner connection.
/// This ensures that connection/transaction association checks performed by EF Core
/// and other ORMs pass correctly when the connection is wrapped.
/// </summary>
internal sealed class TelemetryDbTransaction : DbTransaction
{
    private const string CommitOperation = "commit";
    private const string RollbackOperation = "rollback";
    private const string SavepointOperation = "savepoint";
    private const string RollbackToSavepointOperation = "rollback_to_savepoint";
    private const string ReleaseSavepointOperation = "release_savepoint";

    private readonly DbTransaction _inner;
    private readonly TelemetryDbConnection _connection;
    private bool _disposed;

    internal TelemetryDbTransaction(DbTransaction inner, TelemetryDbConnection connection)
    {
        _inner = inner;
        _connection = connection;
    }

    /// <summary>Gets the unwrapped inner <see cref="DbTransaction"/>.</summary>
    internal DbTransaction InnerTransaction => _inner;

    /// <inheritdoc/>
    public override IsolationLevel IsolationLevel => _inner.IsolationLevel;

    /// <inheritdoc/>
    protected override DbConnection DbConnection => _connection;

#if NET5_0_OR_GREATER
    /// <inheritdoc/>
    public override bool SupportsSavepoints => _inner.SupportsSavepoints;
#endif

    // ── Commit / Rollback ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public override void Commit() =>
        _connection.ExecuteInstrumented(CommitOperation, _inner.Commit);

#if NET6_0_OR_GREATER
    /// <inheritdoc/>
    public override Task CommitAsync(CancellationToken cancellationToken = default) =>
        _connection.ExecuteInstrumentedAsync(CommitOperation, () => _inner.CommitAsync(cancellationToken));
#endif

    /// <inheritdoc/>
    public override void Rollback() =>
        _connection.ExecuteInstrumented(RollbackOperation, _inner.Rollback);

#if NET6_0_OR_GREATER
    /// <inheritdoc/>
    public override Task RollbackAsync(CancellationToken cancellationToken = default) =>
        _connection.ExecuteInstrumentedAsync(RollbackOperation, () => _inner.RollbackAsync(cancellationToken));
#endif

#if NET5_0_OR_GREATER
    // ── Savepoints ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public override void Save(string savepointName) =>
        _connection.ExecuteInstrumented(SavepointOperation, () => _inner.Save(savepointName));

    /// <inheritdoc/>
    public override Task SaveAsync(string savepointName, CancellationToken cancellationToken = default) =>
        _connection.ExecuteInstrumentedAsync(SavepointOperation, () => _inner.SaveAsync(savepointName, cancellationToken));

    /// <inheritdoc/>
    public override void Rollback(string savepointName) =>
        _connection.ExecuteInstrumented(RollbackToSavepointOperation, () => _inner.Rollback(savepointName));

    /// <inheritdoc/>
    public override Task RollbackAsync(string savepointName, CancellationToken cancellationToken = default) =>
        _connection.ExecuteInstrumentedAsync(RollbackToSavepointOperation, () => _inner.RollbackAsync(savepointName, cancellationToken));

    /// <inheritdoc/>
    public override void Release(string savepointName) =>
        _connection.ExecuteInstrumented(ReleaseSavepointOperation, () => _inner.Release(savepointName));

    /// <inheritdoc/>
    public override Task ReleaseAsync(string savepointName, CancellationToken cancellationToken = default) =>
        _connection.ExecuteInstrumentedAsync(ReleaseSavepointOperation, () => _inner.ReleaseAsync(savepointName, cancellationToken));
#endif

    // ── Disposal ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

#if NET5_0_OR_GREATER
    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            await _inner.DisposeAsync().ConfigureAwait(false);
        }

        await base.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
#endif
}
