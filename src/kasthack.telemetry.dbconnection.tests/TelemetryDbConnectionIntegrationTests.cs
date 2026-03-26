using System.Transactions;

using kasthack.telemetry.dbconnection.Decorators;

using Xunit;
using Xunit.Sdk;

namespace kasthack.telemetry.dbconnection.tests;
#pragma warning disable CA2100
/// <summary>
/// Provider-agnostic integration test suite for <see cref="TelemetryDbConnection"/>.
/// Concrete subclasses supply the connection and handle schema setup/teardown.
/// </summary>
public abstract class TelemetryDbConnectionIntegrationTests : IDisposable
{
    /// <summary>Opens a fully connected, telemetry-wrapped <see cref="TelemetryDbConnection"/>.</summary>
    protected abstract TelemetryDbConnection OpenConnection();

    /// <summary>
    /// Name of the test table used by all queries in this class.
    /// Defaults to <c>items</c>; override when the provider uses a shared server
    /// and requires a unique table name per run to avoid cross-test interference.
    /// </summary>
    protected virtual string TableName => "items";

    /// <summary>
    /// Whether the provider supports <see cref="System.Transactions.Transaction"/> enlistment.
    /// Tests guarded by this flag call <see cref="Assert.Skip(string)"/> at runtime when <see langword="false"/>.
    /// </summary>
    protected virtual bool SupportsEnlistTransaction => false;

    public abstract void Dispose();

    // ── Commands return results ──────────────────────────────────────────────

    [Fact]
    public void ExecuteNonQueryReturnsAffectedRowCount()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"INSERT INTO {TableName} (id, value) VALUES (1, 'a')";
        Assert.Equal(1, cmd.ExecuteNonQuery());
    }

    [Fact]
    public async Task ExecuteNonQueryAsyncReturnsAffectedRowCount()
    {
        await using var conn = OpenConnection();
        var cmd = conn.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = $"INSERT INTO {TableName} (id, value) VALUES (2, 'b')";
            Assert.Equal(1, await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task ExecuteScalarReturnsValue()
    {
        await using var conn = OpenConnection();
        await using var seed = conn.CreateCommand();
        seed.CommandText = $"INSERT INTO {TableName} (id, value) VALUES (10, 'hello')";
        seed.ExecuteNonQuery();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT value FROM {TableName} WHERE id = 10";
        Assert.Equal("hello", cmd.ExecuteScalar());
    }

    [Fact]
    public async Task ExecuteScalarAsyncReturnsValue()
    {
        using var conn = OpenConnection();
        var seed = conn.CreateCommand();
        await using (seed.ConfigureAwait(false))
        {
            seed.CommandText = $"INSERT INTO {TableName} (id, value) VALUES (11, 'world')";
            await seed.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var cmd = conn.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = $"SELECT value FROM {TableName} WHERE id = 11";
            Assert.Equal("world", await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        }
    }

    // ── DataReader works ─────────────────────────────────────────────────────

    [Fact]
    public void ExecuteReaderReadsMultipleRows()
    {
        using var conn = OpenConnection();
        using var seed = conn.CreateCommand();
        seed.CommandText = $"INSERT INTO {TableName} (id, value) VALUES (20, 'x'), (21, 'y'), (22, 'z')";
        seed.ExecuteNonQuery();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT id, value FROM {TableName} WHERE id BETWEEN 20 AND 22 ORDER BY id";
        using var reader = cmd.ExecuteReader();

        Assert.True(reader.Read());
        Assert.Equal(20L, reader.GetInt64(0));
        Assert.Equal("x", reader.GetString(1));

        Assert.True(reader.Read());
        Assert.Equal(21L, reader.GetInt64(0));
        Assert.Equal("y", reader.GetString(1));

        Assert.True(reader.Read());
        Assert.Equal(22L, reader.GetInt64(0));
        Assert.Equal("z", reader.GetString(1));

        Assert.False(reader.Read());
    }

    [Fact]
    public async Task ExecuteReaderAsyncReadsMultipleRows()
    {
        using var conn = OpenConnection();
        var seed = conn.CreateCommand();
        await using (seed.ConfigureAwait(true))
        {
            seed.CommandText = $"INSERT INTO {TableName} (id, value) VALUES (30, 'p'), (31, 'q')";
            await seed.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var cmd = conn.CreateCommand();
        await using (cmd.ConfigureAwait(true))
        {
            cmd.CommandText = $"SELECT id, value FROM {TableName} WHERE id BETWEEN 30 AND 31 ORDER BY id";
            var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            await using (reader.ConfigureAwait(true))
            {

                Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
                Assert.Equal(30L, reader.GetInt64(0));
                Assert.Equal("p", reader.GetString(1));

                Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
                Assert.Equal(31L, reader.GetInt64(0));
                Assert.Equal("q", reader.GetString(1));

                Assert.False(await reader.ReadAsync(TestContext.Current.CancellationToken));
            }
        }
    }

    // ── Transactions work ────────────────────────────────────────────────────

    [Fact]
    public void TransactionCommitPersistsChanges()
    {
        using var conn = OpenConnection();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"INSERT INTO {TableName} (id, value) VALUES (40, 'committed')";
        cmd.ExecuteNonQuery();
        tx.Commit();

        using var check = conn.CreateCommand();
        check.CommandText = $"SELECT COUNT(*) FROM {TableName} WHERE id = 40";
        Assert.Equal(1L, Convert.ToInt64(check.ExecuteScalar()));
    }

    [Fact]
    public void TransactionRollbackDoesNotPersistChanges()
    {
        using var conn = OpenConnection();
        using (var tx = conn.BeginTransaction())
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"INSERT INTO {TableName} (id, value) VALUES (50, 'rolled-back')";
            cmd.ExecuteNonQuery();
            tx.Rollback();
        }

        using var check = conn.CreateCommand();
        check.CommandText = $"SELECT COUNT(*) FROM {TableName} WHERE id = 50";
        Assert.Equal(0L, Convert.ToInt64(check.ExecuteScalar()));
    }

    [Fact]
    public async Task TransactionCommitAsyncPersistsChanges()
    {
        using var conn = OpenConnection();
        var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using (tx.ConfigureAwait(false))
        {
            var cmd = conn.CreateCommand();
            await using (cmd.ConfigureAwait(false))
            {
                cmd.Transaction = tx;
                cmd.CommandText = $"INSERT INTO {TableName} (id, value) VALUES (60, 'async-committed')";
                await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }
            await tx.CommitAsync(TestContext.Current.CancellationToken);
        }

        var check = conn.CreateCommand();
        await using (check.ConfigureAwait(true))
        {
            check.CommandText = $"SELECT COUNT(*) FROM {TableName} WHERE id = 60";
            Assert.Equal(1L, Convert.ToInt64(await check.ExecuteScalarAsync(TestContext.Current.CancellationToken)));
        }
    }

    [Fact]
    public async Task TransactionRollbackAsyncDoesNotPersistChanges()
    {
        using var conn = OpenConnection();
        var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using (tx.ConfigureAwait(true))
        {
            var cmd = conn.CreateCommand();
            await using (cmd.ConfigureAwait(true))
            {
                cmd.Transaction = tx;
                cmd.CommandText = $"INSERT INTO {TableName} (id, value) VALUES (70, 'async-rolled-back')";
                await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }
            await tx.RollbackAsync(TestContext.Current.CancellationToken);
        }

        var check = conn.CreateCommand();
        await using (check.ConfigureAwait(false))
        {
            check.CommandText = $"SELECT COUNT(*) FROM {TableName} WHERE id = 70";
            Assert.Equal(0L, Convert.ToInt64(await check.ExecuteScalarAsync(TestContext.Current.CancellationToken)));
        }
    }

    // ── Batches work ─────────────────────────────────────────────────────────

    [Fact]
    public void CanCreateBatchMirrorsInnerConnection()
    {
        // Verifies that TelemetryDbConnection.CanCreateBatch faithfully delegates to the inner provider.
        using var conn = OpenConnection();
        Assert.Equal(conn.InnerConnection.CanCreateBatch, conn.CanCreateBatch);
    }

    [Fact]
    public void BatchExecuteNonQueryInsertsMultipleRows()
    {
        using var conn = OpenConnection();
        if (!conn.CanCreateBatch)
        {
            throw SkipException.ForSkip($"{conn.InnerConnection.GetType().Name} does not support DbBatch");
        }

        using var batch = conn.CreateBatch();
        var c1 = batch.CreateBatchCommand();
        c1.CommandText = $"INSERT INTO {TableName} (id, value) VALUES (80, 'batch1')";
        batch.BatchCommands.Add(c1);
        var c2 = batch.CreateBatchCommand();
        c2.CommandText = $"INSERT INTO {TableName} (id, value) VALUES (81, 'batch2')";
        batch.BatchCommands.Add(c2);
        batch.ExecuteNonQuery();

        using var check = conn.CreateCommand();
        check.CommandText = $"SELECT COUNT(*) FROM {TableName} WHERE id IN (80, 81)";
        Assert.Equal(2L, Convert.ToInt64(check.ExecuteScalar()));
    }

    [Fact]
    public async Task BatchExecuteNonQueryAsyncInsertsMultipleRows()
    {
        using var conn = OpenConnection();
        if (!conn.CanCreateBatch)
        {
            throw SkipException.ForSkip($"{conn.InnerConnection.GetType().Name} does not support DbBatch");
        }
        var batch = conn.CreateBatch();
        await using (batch.ConfigureAwait(true))
        {
            var c1 = batch.CreateBatchCommand();
            c1.CommandText = $"INSERT INTO {TableName} (id, value) VALUES (90, 'async-batch1')";
            batch.BatchCommands.Add(c1);
            var c2 = batch.CreateBatchCommand();
            c2.CommandText = $"INSERT INTO {TableName} (id, value) VALUES (91, 'async-batch2')";
            batch.BatchCommands.Add(c2);
            await batch.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var check = conn.CreateCommand();
        await using (check.ConfigureAwait(true))
        {
            check.CommandText = $"SELECT COUNT(*) FROM {TableName} WHERE id IN (90, 91)";
            Assert.Equal(2L, Convert.ToInt64(await check.ExecuteScalarAsync(TestContext.Current.CancellationToken)));
        }
    }

    // ── Enlist transaction works ─────────────────────────────────────────────

    [Fact]
    public void EnlistTransactionWithIncompleteScopeRollsBackChanges()
    {
        using var conn = OpenConnection();
        if (!SupportsEnlistTransaction)
        {
            throw SkipException.ForSkip($"{conn.InnerConnection.GetType().Name} does not support EnlistTransaction");
        }

        using (new TransactionScope())
        {
            conn.EnlistTransaction(Transaction.Current);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"INSERT INTO {TableName} (id, value) VALUES (100, 'enlisted')";
            cmd.ExecuteNonQuery();
            // scope not completed — rolled back on dispose
        }

        using var check = conn.CreateCommand();
        check.CommandText = $"SELECT COUNT(*) FROM {TableName} WHERE id = 100";
        Assert.Equal(0L, Convert.ToInt64(check.ExecuteScalar()));
    }

    [Fact]
    public void EnlistTransactionWithCompletedScopePersistsChanges()
    {
        using var conn = OpenConnection();
        if (!SupportsEnlistTransaction)
        {
            throw SkipException.ForSkip($"{conn.InnerConnection.GetType().Name} does not support EnlistTransaction");
        }

        using (var scope = new TransactionScope())
        {
            conn.EnlistTransaction(Transaction.Current);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"INSERT INTO {TableName} (id, value) VALUES (101, 'enlisted-committed')";
            cmd.ExecuteNonQuery();
            scope.Complete();
        }

        using var check = conn.CreateCommand();
        check.CommandText = $"SELECT COUNT(*) FROM {TableName} WHERE id = 101";
        Assert.Equal(1L, Convert.ToInt64(check.ExecuteScalar()));
    }
}
#pragma warning restore CA2100
