using kasthack.telemetry.dbconnection.Decorators;
using kasthack.telemetry.dbconnection.Options;

using Microsoft.Data.SqlClient;

using Xunit.Sdk;

namespace kasthack.telemetry.dbconnection.tests;
#pragma warning disable CA2100 // Constants

/// <summary>
/// Runs the shared integration suite against a real SQL Server instance.
/// Set the <c>MSSQL_CONNECTION_STRING</c> environment variable to a valid SQL Server connection string.
/// All tests are skipped when the variable is absent.
/// </summary>
/// <remarks>
/// The constructor creates a session-scoped table with a unique name; <see cref="Dispose"/> drops it.
/// This ensures parallel test runs on a shared server do not interfere with each other.
/// SqlClient 6+ supports DbBatch (<see cref="CanCreateBatch"/> = <see langword="true"/>) and
/// EnlistTransaction, so both feature groups run without skipping.
/// </remarks>
public sealed class TelemetryDbConnectionSqlClientIntegrationTests
    : TelemetryDbConnectionIntegrationTests
{
    private static readonly string? _connectionString =
        Environment.GetEnvironmentVariable("MSSQL_CONNECTION_STRING");

    private readonly string _tableId = Guid.NewGuid().ToString("N");

    // Table name is unique per run to avoid cross-test interference on a shared server.
    protected override string TableName => $"items_{_tableId}";

    // SqlClient supports EnlistTransaction via System.Transactions.
    protected override bool SupportsEnlistTransaction => true;

    public TelemetryDbConnectionSqlClientIntegrationTests()
    {
        if (_connectionString is null)
        {
            return;
        }

        using var conn = new SqlConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            CREATE TABLE {TableName}
            (
                id    BIGINT        NOT NULL PRIMARY KEY,
                value NVARCHAR(255) NOT NULL
            )
            """;
        cmd.ExecuteNonQuery();
    }

    public override void Dispose()
    {
        if (_connectionString is null)
        {
            return;
        }

        using var conn = new SqlConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DROP TABLE IF EXISTS {TableName}";
        cmd.ExecuteNonQuery();
    }

    protected override TelemetryDbConnection OpenConnection()
    {
        if (_connectionString is null)
        {
            throw SkipException.ForSkip("MSSQL_CONNECTION_STRING environment variable is not set");
        }

#pragma warning disable CA2000 // Constructor
        var conn = new TelemetryDbConnectionFactory(
            new TelemetryDbConnectionOptions { EmitTraces = true, EmitMetrics = true })
            .Wrap(new SqlConnection(_connectionString));
#pragma warning restore CA2000
        conn.Open();
        return conn;
    }
}

#pragma warning restore CA2100 // 
