namespace kasthack.telemetry.dbconnection;

/// <summary>OpenTelemetry semantic convention tag names for database operations.</summary>
internal static class DbSemanticConventions
{
    internal const string DbSystem = "db.system";
    internal const string DbName = "db.name";
    internal const string DbStatement = "db.statement";
    internal const string DbOperation = "db.operation";
    internal const string ServerAddress = "server.address";
    internal const string ServerPort = "server.port";
    internal const string ErrorType = "error.type";

    internal const string ConnectOperation = "connect";
}
