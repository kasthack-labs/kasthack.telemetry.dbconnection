using System.Diagnostics;

using kasthack.telemetry.dbconnection.ef;
using kasthack.telemetry.dbconnection.Options;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace kasthack.telemetry.dbconnection.tests;

internal sealed class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
    public DbSet<TestEntity> Items { get; set; } = null!;
}

internal sealed class TestEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class TelemetryDbConnectionEfTests : IDisposable
{
    // Each test instance gets a unique in-memory DB name to prevent cross-test interference.
    private readonly string _dbName = $"ef_test_{Guid.NewGuid():N}";
    private readonly string _connectionString;
    // Keepalive connection prevents the named in-memory database from being dropped.
    private readonly SqliteConnection _keepalive;

    public TelemetryDbConnectionEfTests()
    {
        _connectionString = $"Data Source={_dbName};Mode=Memory;Cache=Shared";
        _keepalive = new SqliteConnection(_connectionString);
        _keepalive.Open();
    }

    public void Dispose() => _keepalive.Dispose();

    private DbContextOptions<TestDbContext> CreateOptions(TelemetryDbConnectionOptions? telemetryOptions = null) =>
        new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(_connectionString)
            .AddInterceptors(new TelemetryDbConnectionInterceptor(telemetryOptions))
            .Options;

    [Fact]
    public async Task BasicQueryDoesNotThrow()
    {
        var options = CreateOptions(new TelemetryDbConnectionOptions { EmitTraces = true, EmitMetrics = true });
        var ctx = new TestDbContext(options);
        await using (ctx.ConfigureAwait(true))
        {
            await ctx.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            ctx.Items.Add(new TestEntity { Name = "test" });
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            var count = await ctx.Items.CountAsync(cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
            Assert.Equal(1, count);
        }
    }

    [Fact]
    public async Task TransactionCommitWorks()
    {
        var options = CreateOptions(new TelemetryDbConnectionOptions { EmitTraces = true, EmitMetrics = true });
        var ctx = new TestDbContext(options);
        await using (ctx.ConfigureAwait(true))
        {
            await ctx.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
            var tx = (await ctx.Database.BeginTransactionAsync(TestContext.Current.CancellationToken).ConfigureAwait(true));
            await using (tx.ConfigureAwait(true))
            {
                ctx.Items.Add(new TestEntity { Name = "tx-item" });
                await ctx.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
                await tx.CommitAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

                var count = await ctx.Items.CountAsync(cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
                Assert.Equal(1, count);
            }
        }
    }

    [Fact]
    public async Task TransactionRollbackWorks()
    {
        var options = CreateOptions(new TelemetryDbConnectionOptions { EmitTraces = true, EmitMetrics = true });
        var ctx = new TestDbContext(options);
        await using (ctx.ConfigureAwait(true))
        {
            await ctx.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
            var tx = await ctx.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            await using (tx.ConfigureAwait(true))
            {
                ctx.Items.Add(new TestEntity { Name = "rollback-item" });
                await ctx.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
                await tx.RollbackAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
            }

            ctx.ChangeTracker.Clear();
            var count = await ctx.Items.CountAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
            Assert.Equal(0, count);
        }
    }

    [Fact]
    public async Task BasicQueryEmitsActivitiesWhenTracesEnabled()
    {
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TelemetryDbConnectionInstrumentation.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);

        var options = CreateOptions(new TelemetryDbConnectionOptions { EmitTraces = true, EmitMetrics = false });
        var ctx = new TestDbContext(options);
        await using (ctx.ConfigureAwait(true))
        {
            await ctx.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
            await ctx.Items.CountAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.NotEmpty(activities);
        }
    }

    [Fact]
    public async Task BasicQueryDoesNotEmitActivitiesWhenTracesDisabled()
    {
        var activities = new List<Activity>();
        var options = CreateOptions(new TelemetryDbConnectionOptions { EmitTraces = false, EmitMetrics = false });
        var ctx = new TestDbContext(options);
        await using (ctx.ConfigureAwait(true))
        {
            await ctx.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            // Register listener AFTER setup to avoid capturing schema-creation activities from other tests.
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == TelemetryDbConnectionInstrumentation.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = activities.Add,
            };
            ActivitySource.AddActivityListener(listener);

            await ctx.Items.CountAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.Empty(activities);
        }
    }
}
