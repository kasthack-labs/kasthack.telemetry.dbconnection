using kasthack.telemetry.dbconnection.Decorators;
using kasthack.telemetry.dbconnection.Options;

using Microsoft.Data.Sqlite;

namespace kasthack.telemetry.dbconnection.tests;

public sealed class TelemetryDbConnectionSqliteIntegrationTests
    : TelemetryDbConnectionIntegrationTests
{
    // TableName defaults to "items"; isolation comes from the unique in-memory database.
    // SQLite does not support DbBatch or EnlistTransaction — those tests skip at runtime.

    private readonly string _connectionString;
    private readonly SqliteConnection _keepalive;

    public TelemetryDbConnectionSqliteIntegrationTests()
    {
        var dbName = $"integration_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        _keepalive = new SqliteConnection(_connectionString);
        _keepalive.Open();
        using var cmd = _keepalive.CreateCommand();
        cmd.CommandText = "CREATE TABLE items (id INTEGER PRIMARY KEY, value TEXT NOT NULL)";
        cmd.ExecuteNonQuery();
    }

    public override void Dispose() => _keepalive.Dispose();

    protected override TelemetryDbConnection OpenConnection()
    {
#pragma warning disable CA2000 // Basically, a constructor
        var conn = new TelemetryDbConnectionFactory(
            new TelemetryDbConnectionOptions { EmitTraces = true, EmitMetrics = true })
            .Wrap(new SqliteConnection(_connectionString));
#pragma warning restore CA2000 // 
        conn.Open();
        return conn;
    }
}
