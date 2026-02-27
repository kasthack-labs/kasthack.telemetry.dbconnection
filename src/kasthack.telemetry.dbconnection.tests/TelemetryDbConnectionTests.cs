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
    private static TelemetryDbConnection CreateConnection(MockDbConnection mock, TelemetryDbConnectionOptions options) =>
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

    [Fact]
    public void EmitTraces_False_DoesNotCreateActivity()
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        var mock = CreateMockDbConnection();
        using var conn = CreateConnection(mock, new TelemetryDbConnectionOptions { EmitTraces = false, EmitMetrics = false });
        conn.Open();

        Assert.Empty(activities);
    }

    [Fact]
    public void EmitMetrics_False_DoesNotRecordDuration()
    {
        var measurements = new List<double>();
        using var meterListener = CreateMeterListener(measurements);

        var mock = CreateMockDbConnection();
        using var conn = CreateConnection(mock, new TelemetryDbConnectionOptions { EmitTraces = false, EmitMetrics = false });
        conn.Open();

        Assert.Empty(measurements);
    }

    [Fact]
    public void EmitMetrics_True_RecordsDuration()
    {
        var measurements = new List<double>();
        using var meterListener = CreateMeterListener(measurements);

        var mock = CreateMockDbConnection();
        using var conn = CreateConnection(mock, new TelemetryDbConnectionOptions { EmitTraces = false, EmitMetrics = true });
        conn.Open();

        Assert.Single(measurements);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(50)]
    public void EmitMetrics_DurationMatchesActualTime(int delayMs)
    {
        var measurements = new List<double>();
        using var meterListener = CreateMeterListener(measurements);

        var mock = new MockDbConnection { CommandDelay = TimeSpan.FromMilliseconds(delayMs) };
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
    public async Task EmitMetrics_DurationMatchesActualTime_Async(int delayMs)
    {
        var measurements = new List<double>();
        using var meterListener = CreateMeterListener(measurements);

        var mock = new MockDbConnection { CommandDelay = TimeSpan.FromMilliseconds(delayMs) };
        using var conn = CreateConnection(mock, new TelemetryDbConnectionOptions { EmitTraces = false, EmitMetrics = true });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await conn.OpenAsync();
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
    public void AllSyncOperations_EmitTraces_CreateActivity(string expectedOperation, Action<TelemetryDbConnection> runOperation)
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        var mock = CreateMockDbConnection();
        using var conn = CreateConnection(mock, new TelemetryDbConnectionOptions { EmitTraces = true, EmitMetrics = false });
        runOperation(conn);

        Assert.Contains(activities, a => a.DisplayName == expectedOperation);
    }

    public static TheoryData<string, Func<TelemetryDbConnection, Task>> AsyncOperations =>
        new()
        {
            { "connect",  async c => await c.OpenAsync().ConfigureAwait(false) },
            { "SELECT",   async c => { await using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT 1"; await cmd.ExecuteNonQueryAsync().ConfigureAwait(false); } },
            { "SELECT",   async c => { await using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT 1"; await cmd.ExecuteScalarAsync().ConfigureAwait(false); } },
            { "SELECT",   async c => { await using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT 1"; await using var r = await cmd.ExecuteReaderAsync().ConfigureAwait(false); } },
            { "prepare",  async c => { await using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT 1"; await cmd.PrepareAsync().ConfigureAwait(false); } },
            { "commit",   async c => { await using var tx = await c.BeginTransactionAsync().ConfigureAwait(false); await tx.CommitAsync().ConfigureAwait(false); } },
            { "rollback", async c => { await using var tx = await c.BeginTransactionAsync().ConfigureAwait(false); await tx.RollbackAsync().ConfigureAwait(false); } },
            { "change_database", async c => await c.ChangeDatabaseAsync("MockDb").ConfigureAwait(false) },
            { "get_schema",      async c => await c.GetSchemaAsync().ConfigureAwait(false) },
            { "begin_transaction", async c => { await using var tx = await c.BeginTransactionAsync().ConfigureAwait(false); await tx.RollbackAsync().ConfigureAwait(false); } },
            { "batch",           async c => { await using var b = c.CreateBatch(); await b.ExecuteNonQueryAsync().ConfigureAwait(false); } },
        };

    [Theory]
    [MemberData(nameof(AsyncOperations))]
    public async Task AllAsyncOperations_EmitTraces_CreateActivity(string expectedOperation, Func<TelemetryDbConnection, Task> runOperation)
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        var mock = CreateMockDbConnection();
        using var conn = CreateConnection(mock, new TelemetryDbConnectionOptions { EmitTraces = true, EmitMetrics = false });
        await runOperation(conn);

        Assert.Contains(activities, a => a.DisplayName == expectedOperation);
    }

    [Fact]
    public void EnrichActivity_IsCalled_WithCommand()
    {
        DbCommand? capturedCommand = null;
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        var mock = CreateMockDbConnection();
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
    public void EnrichActivity_IsCalledWithNull_ForConnect()
    {
        var callbackCalled = false;
        DbCommand? capturedCommand = null;
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        var mock = CreateMockDbConnection();
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
    public void EnrichMetrics_IsCalled_WithCommand()
    {
        var enrichMetricsCalled = false;
        var measurements = new List<double>();
        using var meterListener = CreateMeterListener(measurements);

        var mock = CreateMockDbConnection();
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
    public void EnrichActivity_ExceptionInCallback_OperationStillSucceeds()
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        var mock = CreateMockDbConnection();
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
    public void EnrichMetrics_ExceptionInCallback_OperationStillSucceeds()
    {
        var measurements = new List<double>();
        using var meterListener = CreateMeterListener(measurements);

        var mock = CreateMockDbConnection();
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
    public void CaptureStatements_None_DoesNotTagStatement()
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        var mock = CreateMockDbConnection();
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
    public void CaptureStatements_All_TagsStatement()
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        var mock = CreateMockDbConnection();
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
    public void CaptureStatements_StoredProcedures_TagsStoredProcedureStatement()
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        var mock = CreateMockDbConnection();
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
    public void CaptureStatements_StoredProcedures_DoesNotTagTextStatement()
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        var mock = CreateMockDbConnection();
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
    public void CaptureStatements_Text_TagsTextStatement()
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        var mock = CreateMockDbConnection();
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
    public void CaptureStatements_Text_DoesNotTagStoredProcedureStatement()
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        var mock = CreateMockDbConnection();
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
    public void Close_WhenTrackConnectionManagementAll_EmitsActivity()
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        var mock = CreateMockDbConnection();
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
    public void Open_WhenTrackConnectionManagementNone_DoesNotEmitActivity()
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        var mock = CreateMockDbConnection();
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

