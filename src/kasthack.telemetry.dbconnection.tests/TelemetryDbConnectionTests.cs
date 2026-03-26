using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.Metrics;

using kasthack.telemetry.dbconnection.Decorators;
using kasthack.telemetry.dbconnection.Options;

using Xunit;

namespace kasthack.telemetry.dbconnection.tests;

public sealed class TelemetryDbConnectionTests
{
    private static TelemetryDbConnection CreateConnection(DbConnection mock, TelemetryDbConnectionOptions options) =>
        new TelemetryDbConnectionFactory(options).Wrap(mock);

    private static ActivityListener CreateActivityListener(List<Activity> activities) =>
        new ActivityListener
        {
            ShouldListenTo = source => source.Name == TelemetryDbConnectionInstrumentation.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activities.Add,
        };

    private static MeterListener CreateMeterListener(List<double> measurements)
    {
        var ml = new MeterListener();
        ml.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == TelemetryDbConnectionInstrumentation.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        ml.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
            measurements.Add(measurement));
        ml.Start();
        return ml;
    }

    private static DbConnection CreateMockDbConnection() => new MockDbConnection();

    [Fact]
    public void EmitTracesFalseDoesNotCreateActivity()
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        using var mock = CreateMockDbConnection();
        using var conn = CreateConnection(mock, new TelemetryDbConnectionOptions { EmitTraces = false, EmitMetrics = false });
        conn.Open();

        Assert.Empty(activities);
    }

    [Fact]
    public void EmitMetricsFalseDoesNotRecordDuration()
    {
        var measurements = new List<double>();
        using var meterListener = CreateMeterListener(measurements);

        using var mock = CreateMockDbConnection();
        using var conn = CreateConnection(mock, new TelemetryDbConnectionOptions { EmitTraces = false, EmitMetrics = false });
        conn.Open();

        Assert.Empty(measurements);
    }

    [Fact]
    public void EmitMetricsTrueRecordsDuration()
    {
        var measurements = new List<double>();
        using var meterListener = CreateMeterListener(measurements);

        using var mock = CreateMockDbConnection();
        using var conn = CreateConnection(mock, new TelemetryDbConnectionOptions { EmitTraces = false, EmitMetrics = true });
        conn.Open();

        Assert.Single(measurements);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(50)]
    public void EmitMetricsDurationMatchesActualTime(int delayMs)
    {
        var measurements = new List<double>();
        using var meterListener = CreateMeterListener(measurements);

        using var mock = new MockDbConnection { CommandDelay = TimeSpan.FromMilliseconds(delayMs) };
        using var conn = CreateConnection(mock, new TelemetryDbConnectionOptions { EmitTraces = false, EmitMetrics = true });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        conn.Open();
        sw.Stop();

        var recorded = Assert.Single(measurements);
        var actualSeconds = sw.Elapsed.TotalSeconds;
        var expectedSeconds = delayMs / 1000.0;

        Assert.True(recorded >= expectedSeconds - 0.001,
            $"Recorded {recorded}s should be >= expected {expectedSeconds}s");
        Assert.True(Math.Abs(recorded - actualSeconds) < 0.1,
            $"Difference between recorded {recorded:F4}s and actual {actualSeconds:F4}s exceeds 100ms tolerance");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(50)]
    public async Task EmitMetricsDurationMatchesActualTimeAsync(double delayMs)
    {
        var measurements = new List<double>();
        using var meterListener = CreateMeterListener(measurements);

        using var mock = new MockDbConnection { CommandDelay = TimeSpan.FromMilliseconds(delayMs) };
        using var conn = CreateConnection(mock, new TelemetryDbConnectionOptions { EmitTraces = false, EmitMetrics = true });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        sw.Stop();

        var recorded = Assert.Single(measurements);
        var actualSeconds = sw.Elapsed.TotalSeconds;
        var expectedSeconds = delayMs / 1000.0;

        Assert.True(recorded >= expectedSeconds - 0.001,
            $"Recorded {recorded}s should be >= expected {expectedSeconds}s");
        Assert.True(Math.Abs(recorded - actualSeconds) < 0.1,
            $"Difference between recorded {recorded:F4}s and actual {actualSeconds:F4}s exceeds 100ms tolerance");
    }

    public static TheoryData<string, Action<TelemetryDbConnection>> SyncOperations =>
        new()
        {
            { "connect",  c => c.Open() },
            { "SELECT",   c => { using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT 1"; cmd.ExecuteNonQuery(); } },
            { "INSERT",   c => { using var cmd = c.CreateCommand(); cmd.CommandText = "INSERT INTO t VALUES(1)"; cmd.ExecuteNonQuery(); } },
            { "SELECT",   c => { using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT 1"; cmd.ExecuteScalar(); } },
            { "SELECT",   c => { using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT 1"; using var r = cmd.ExecuteReader(); } },
            { "prepare",  c => { using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT 1"; cmd.Prepare(); } },
            { "cancel",   c => { using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT 1"; cmd.Cancel(); } },
            { "commit",   c => { using var tx = c.BeginTransaction(); tx.Commit(); } },
            { "rollback", c => { using var tx = c.BeginTransaction(); tx.Rollback(); } },
            { "savepoint",             c => { using var tx = c.BeginTransaction(); tx.Save("sp1"); tx.Rollback(); } },
            { "rollback_to_savepoint", c => { using var tx = c.BeginTransaction(); tx.Save("sp1"); tx.Rollback("sp1"); tx.Rollback(); } },
            { "release_savepoint",     c => { using var tx = c.BeginTransaction(); tx.Save("sp1"); tx.Release("sp1"); tx.Rollback(); } },
            { "change_database", c => c.ChangeDatabase("MockDb") },
            { "get_schema",      c => c.GetSchema() },
            { "begin_transaction", c => { using var tx = c.BeginTransaction(); tx.Rollback(); } },
            { "batch",           c => { using var b = c.CreateBatch(); b.ExecuteNonQuery(); } },
        };

    [Theory]
    [MemberData(nameof(SyncOperations))]
    public void AllSyncOperationsEmitTracesCreateActivity(string expectedOperation, Action<TelemetryDbConnection> runOperation)
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        using var mock = CreateMockDbConnection();
        using var conn = CreateConnection(mock, new TelemetryDbConnectionOptions { EmitTraces = true, EmitMetrics = false });
        runOperation(conn);

        Assert.Contains(activities, a => a.DisplayName == expectedOperation);
    }

    public static TheoryData<string, Func<TelemetryDbConnection, Task>> AsyncOperations =>
        new()
        {
            {
                "connect",
                async c => await c.OpenAsync().ConfigureAwait(false) },
            {
                "SELECT",
                async c =>
                {
                    var cmd = c.CreateCommand();
                    await using (cmd.ConfigureAwait(false))
                    {
                        cmd.CommandText = "SELECT 1";
                        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                    }
                }
            },
            {
                "SELECT",
                async c =>
                {
                    var cmd = c.CreateCommand();
                    await using (cmd.ConfigureAwait(false))
                    {
                        cmd.CommandText = "SELECT 1";
                        await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                    }
                }
            },
            {
                "SELECT",
                async c =>
                {
                    var cmd = c.CreateCommand();
                    await using (cmd.ConfigureAwait(false))
                    {
                        cmd.CommandText = "SELECT 1";
                        var r = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
                        await using (r.ConfigureAwait(false)) { }
                    }
                }
            },
            {
                "prepare",
                async c =>
                {
                    var cmd = c.CreateCommand();
                    await using (cmd.ConfigureAwait(false))
                    {
                        cmd.CommandText = "SELECT 1";
                        await cmd.PrepareAsync().ConfigureAwait(false);
                    }
                }
            },
            {
                "commit",
                async c =>
                {
                    var tx = await c.BeginTransactionAsync().ConfigureAwait(false);
                    await using (tx.ConfigureAwait(false))
                    {
                        await tx.CommitAsync().ConfigureAwait(false);
                    }
                }
            },
            {
                "rollback",
                async c =>
                {
                    var tx = await c.BeginTransactionAsync().ConfigureAwait(false);
                    await using (tx.ConfigureAwait(false))
                    {
                        await tx.RollbackAsync().ConfigureAwait(false);
                    }
                }
            },
            {
                "change_database",
                async c =>
                {
                    await c.ChangeDatabaseAsync("MockDb").ConfigureAwait(false);
                }
            },
            {
                "get_schema",
                async c =>
                {
                    await c.GetSchemaAsync().ConfigureAwait(false);
                }
            },
            {
                "begin_transaction",
                async c =>
                {
                    var tx = await c.BeginTransactionAsync().ConfigureAwait(false);
                    await using (tx.ConfigureAwait(false))
                    {
                        await tx.RollbackAsync().ConfigureAwait(false);
                    }
                }
            },
            {
                "batch",
                async c =>
                {
                    var b = c.CreateBatch();
                    await using (b.ConfigureAwait(true))
                    {
                        await b.ExecuteNonQueryAsync().ConfigureAwait(false);
                    }
                }
            },
        };

    [Theory]
    [MemberData(nameof(AsyncOperations))]
    public async Task AllAsyncOperationsEmitTracesCreateActivity(string expectedOperation, Func<TelemetryDbConnection, Task> runOperation)
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        using var mock = CreateMockDbConnection();
        using var conn = CreateConnection(mock, new TelemetryDbConnectionOptions { EmitTraces = true, EmitMetrics = false });
        await runOperation(conn);

        Assert.Contains(activities, a => a.DisplayName == expectedOperation);
    }

    [Fact]
    public void EnrichActivityIsCalledWithCommand()
    {
        DbCommand? capturedCommand = null;
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        using var mock = CreateMockDbConnection();
        using var conn = CreateConnection(mock, new TelemetryDbConnectionOptions
        {
            EmitTraces = true,
            EmitMetrics = false,
            EnrichActivity = (activity, cmd) => capturedCommand = cmd,
        });
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        cmd.ExecuteNonQuery();

        Assert.NotNull(capturedCommand);
        Assert.Equal("SELECT 1", capturedCommand.CommandText);
    }

    [Fact]
    public void EnrichActivityIsCalledWithNullForConnect()
    {
        var callbackCalled = false;
        DbCommand? capturedCommand = null;
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        using var mock = CreateMockDbConnection();
        using var conn = CreateConnection(mock, new TelemetryDbConnectionOptions
        {
            EmitTraces = true,
            EmitMetrics = false,
            EnrichActivity = (activity, cmd) =>
            {
                callbackCalled = true;
                capturedCommand = cmd;
            },
        });
        conn.Open();

        Assert.True(callbackCalled);
        Assert.Null(capturedCommand);
    }

    [Fact]
    public void EnrichMetricsIsCalledWithCommand()
    {
        var enrichMetricsCalled = false;
        var measurements = new List<double>();
        using var meterListener = CreateMeterListener(measurements);

        using var mock = CreateMockDbConnection();
        using var conn = CreateConnection(mock, new TelemetryDbConnectionOptions
        {
            EmitTraces = false,
            EmitMetrics = true,
            EnrichMetrics = (tags, cmd) => enrichMetricsCalled = true,
        });
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        cmd.ExecuteNonQuery();

        Assert.True(enrichMetricsCalled);
    }

    [Fact]
    public void EnrichActivityExceptionInCallbackOperationStillSucceeds()
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        using var mock = CreateMockDbConnection();
        using var conn = CreateConnection(mock, new TelemetryDbConnectionOptions
        {
            EmitTraces = true,
            EmitMetrics = false,
            EnrichActivity = (activity, cmd) => throw new InvalidOperationException("test"),
        });
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        var exception = Record.Exception(() => cmd.ExecuteNonQuery());
        Assert.Null(exception);
    }

    [Fact]
    public void EnrichMetricsExceptionInCallbackOperationStillSucceeds()
    {
        var measurements = new List<double>();
        using var meterListener = CreateMeterListener(measurements);

        using var mock = CreateMockDbConnection();
        using var conn = CreateConnection(mock, new TelemetryDbConnectionOptions
        {
            EmitTraces = false,
            EmitMetrics = true,
            EnrichMetrics = (tags, cmd) => throw new InvalidOperationException("test"),
        });
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        var exception = Record.Exception(() => cmd.ExecuteNonQuery());
        Assert.Null(exception);
    }

    [Fact]
    public void CaptureStatementsNoneDoesNotTagStatement()
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        using var mock = CreateMockDbConnection();
        using var conn = CreateConnection(mock, new TelemetryDbConnectionOptions
        {
            EmitTraces = true,
            EmitMetrics = false,
            CaptureStatements = CaptureStatements.None,
        });
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        cmd.ExecuteNonQuery();

        var activity = Assert.Single(activities);
        Assert.Null(activity.GetTagItem("db.statement"));
    }

    [Fact]
    public void CaptureStatementsAllTagsStatement()
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        using var mock = CreateMockDbConnection();
        using var conn = CreateConnection(mock, new TelemetryDbConnectionOptions
        {
            EmitTraces = true,
            EmitMetrics = false,
            CaptureStatements = CaptureStatements.All,
        });
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        cmd.ExecuteNonQuery();

        var activity = Assert.Single(activities);
        Assert.Equal("SELECT 1", activity.GetTagItem("db.statement"));
    }

    [Fact]
    public void CaptureStatementsStoredProceduresTagsStoredProcedureStatement()
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        using var mock = CreateMockDbConnection();
        using var conn = CreateConnection(mock, new TelemetryDbConnectionOptions
        {
            EmitTraces = true,
            EmitMetrics = false,
            CaptureStatements = CaptureStatements.StoredProcedures,
        });
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "dbo.GetUser";
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.ExecuteNonQuery();

        var activity = Assert.Single(activities);
        Assert.Equal("dbo.GetUser", activity.GetTagItem("db.statement"));
    }

    [Fact]
    public void CaptureStatementsStoredProceduresDoesNotTagTextStatement()
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        using var mock = CreateMockDbConnection();
        using var conn = CreateConnection(mock, new TelemetryDbConnectionOptions
        {
            EmitTraces = true,
            EmitMetrics = false,
            CaptureStatements = CaptureStatements.StoredProcedures,
        });
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        cmd.ExecuteNonQuery();

        var activity = Assert.Single(activities);
        Assert.Null(activity.GetTagItem("db.statement"));
    }

    [Fact]
    public void CaptureStatementsTextTagsTextStatement()
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        using var mock = CreateMockDbConnection();
        using var conn = CreateConnection(mock, new TelemetryDbConnectionOptions
        {
            EmitTraces = true,
            EmitMetrics = false,
            CaptureStatements = CaptureStatements.Text,
        });
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        cmd.ExecuteNonQuery();

        var activity = Assert.Single(activities);
        Assert.Equal("SELECT 1", activity.GetTagItem("db.statement"));
    }

    [Fact]
    public void CaptureStatementsTextDoesNotTagStoredProcedureStatement()
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        using var mock = CreateMockDbConnection();
        using var conn = CreateConnection(mock, new TelemetryDbConnectionOptions
        {
            EmitTraces = true,
            EmitMetrics = false,
            CaptureStatements = CaptureStatements.Text,
        });
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "dbo.GetUser";
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.ExecuteNonQuery();

        var activity = Assert.Single(activities);
        Assert.Null(activity.GetTagItem("db.statement"));
    }

    [Fact]
    public void CloseWhenTrackConnectionManagementAllEmitsActivity()
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        using var mock = CreateMockDbConnection();
        using var conn = CreateConnection(mock, new TelemetryDbConnectionOptions
        {
            EmitTraces = true,
            EmitMetrics = false,
            TrackConnectionManagement = ConnectionManagementTracking.All,
        });
        conn.Open();
        conn.Close();

        Assert.Contains(activities, a => a.DisplayName == "connect");
        Assert.Contains(activities, a => a.DisplayName == "close");
    }

    [Fact]
    public void OpenWhenTrackConnectionManagementNoneDoesNotEmitActivity()
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        using var mock = CreateMockDbConnection();
        using var conn = CreateConnection(mock, new TelemetryDbConnectionOptions
        {
            EmitTraces = true,
            EmitMetrics = false,
            TrackConnectionManagement = ConnectionManagementTracking.None,
        });
        conn.Open();

        Assert.Empty(activities);
    }
}

