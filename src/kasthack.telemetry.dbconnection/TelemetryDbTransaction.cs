using System.Data;
using System.Data.Common;

namespace kasthack.telemetry.dbconnection;

/// <summary>
/// A <see cref="DbTransaction"/> decorator that reports its connection as the owning
/// <see cref="TelemetryDbConnection"/> rather than the underlying inner connection.
/// This ensures that connection/transaction association checks performed by EF Core
/// and other ORMs pass correctly when the connection is wrapped.
/// </summary>
internal sealed class TelemetryDbTransaction : DbTransaction
{
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

    /// <inheritdoc/>
    public override bool SupportsSavepoints => _inner.SupportsSavepoints;

    // ── Commit / Rollback ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public override void Commit() => _inner.Commit();

    /// <inheritdoc/>
    public override Task CommitAsync(CancellationToken cancellationToken = default) =>
        _inner.CommitAsync(cancellationToken);

    /// <inheritdoc/>
    public override void Rollback() => _inner.Rollback();

    /// <inheritdoc/>
    public override Task RollbackAsync(CancellationToken cancellationToken = default) =>
        _inner.RollbackAsync(cancellationToken);

    // ── Savepoints ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public override void Save(string savepointName) => _inner.Save(savepointName);

    /// <inheritdoc/>
    public override Task SaveAsync(string savepointName, CancellationToken cancellationToken = default) =>
        _inner.SaveAsync(savepointName, cancellationToken);

    /// <inheritdoc/>
    public override void Rollback(string savepointName) => _inner.Rollback(savepointName);

    /// <inheritdoc/>
    public override Task RollbackAsync(string savepointName, CancellationToken cancellationToken = default) =>
        _inner.RollbackAsync(savepointName, cancellationToken);

    /// <inheritdoc/>
    public override void Release(string savepointName) => _inner.Release(savepointName);

    /// <inheritdoc/>
    public override Task ReleaseAsync(string savepointName, CancellationToken cancellationToken = default) =>
        _inner.ReleaseAsync(savepointName, cancellationToken);

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
}
