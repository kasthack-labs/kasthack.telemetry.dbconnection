using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.Metrics;
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

        var mock = new MockDbConnection();
        using var conn = CreateConnection(mock, new TelemetryDbConnectionOptions { EmitTraces = false, EmitMetrics = false });
        conn.Open();

        Assert.Empty(activities);
    }

    [Fact]
    public void EmitTraces_True_CreatesActivityForOpen()
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        var mock = new MockDbConnection();
        using var conn = CreateConnection(mock, new TelemetryDbConnectionOptions { EmitTraces = true, EmitMetrics = false });
        conn.Open();

        var activity = Assert.Single(activities);
        Assert.Equal("connect", activity.DisplayName);
    }

    [Fact]
    public void EmitTraces_True_CreatesActivityForExecuteNonQuery()
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        var mock = new MockDbConnection();
        using var conn = CreateConnection(mock, new TelemetryDbConnectionOptions { EmitTraces = true, EmitMetrics = false });
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE foo SET x=1";
        cmd.ExecuteNonQuery();

        var activity = Assert.Single(activities);
        Assert.Equal("UPDATE", activity.DisplayName);
    }

    [Fact]
    public void EmitMetrics_False_DoesNotRecordDuration()
    {
        var measurements = new List<double>();
        using var meterListener = CreateMeterListener(measurements);

        var mock = new MockDbConnection();
        using var conn = CreateConnection(mock, new TelemetryDbConnectionOptions { EmitTraces = false, EmitMetrics = false });
        conn.Open();

        Assert.Empty(measurements);
    }

    [Fact]
    public void EmitMetrics_True_RecordsDuration()
    {
        var measurements = new List<double>();
        using var meterListener = CreateMeterListener(measurements);

        var mock = new MockDbConnection();
        using var conn = CreateConnection(mock, new TelemetryDbConnectionOptions { EmitTraces = false, EmitMetrics = true });
        conn.Open();

        Assert.Single(measurements);
    }

    [Fact]
    public void EmitMetrics_True_DurationAtLeastAsLongAsDelay()
    {
        var measurements = new List<double>();
        using var meterListener = CreateMeterListener(measurements);

        var mock = new MockDbConnection { CommandDelay = TimeSpan.FromMilliseconds(50) };
        using var conn = CreateConnection(mock, new TelemetryDbConnectionOptions { EmitTraces = false, EmitMetrics = true });
        conn.Open();

        var duration = Assert.Single(measurements);
        Assert.True(duration >= 0.045, $"Expected duration >= 0.045s but was {duration}s");
    }

    [Fact]
    public void EnrichActivity_IsCalled_WithCommand()
    {
        DbCommand? capturedCommand = null;
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        var mock = new MockDbConnection();
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

        var mock = new MockDbConnection();
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

        var mock = new MockDbConnection();
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

        var mock = new MockDbConnection();
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

        var mock = new MockDbConnection();
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
    public void CaptureStatements_False_DoesNotTagStatement()
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        var mock = new MockDbConnection();
        using var conn = CreateConnection(mock, new TelemetryDbConnectionOptions
        {
            EmitTraces = true,
            EmitMetrics = false,
            CaptureStatements = false,
        });
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        cmd.ExecuteNonQuery();

        var activity = Assert.Single(activities);
        Assert.Null(activity.GetTagItem("db.statement"));
    }

    [Fact]
    public void CaptureStatements_True_TagsStatement()
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        var mock = new MockDbConnection();
        using var conn = CreateConnection(mock, new TelemetryDbConnectionOptions
        {
            EmitTraces = true,
            EmitMetrics = false,
            CaptureStatements = true,
        });
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        cmd.ExecuteNonQuery();

        var activity = Assert.Single(activities);
        Assert.Equal("SELECT 1", activity.GetTagItem("db.statement"));
    }
}
