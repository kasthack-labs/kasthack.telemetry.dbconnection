using System.Data.Common;

using kasthack.telemetry.dbconnection.Decorators;
using kasthack.telemetry.dbconnection.Options;

using Microsoft.EntityFrameworkCore.Diagnostics;

namespace kasthack.telemetry.dbconnection.ef;

/// <summary>
/// An Entity Framework Core <see cref="DbConnectionInterceptor"/> that wraps each newly created
/// <see cref="DbConnection"/> with a <see cref="TelemetryDbConnection"/> so that all database
/// operations are automatically instrumented with traces and metrics.
/// </summary>
/// <remarks>Register via <c>DbContextOptionsBuilder.AddInterceptors</c>.</remarks>
public sealed class TelemetryDbConnectionInterceptor(TelemetryDbConnectionOptions? options = null)
    : DbConnectionInterceptor
{
    private readonly TelemetryDbConnectionFactory _factory = new(options);

    /// <inheritdoc/>
    public override DbConnection ConnectionCreated(ConnectionCreatedEventData eventData, DbConnection result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result is TelemetryDbConnection ? result : _factory.Wrap(result);
    }
}
