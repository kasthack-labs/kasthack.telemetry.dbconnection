using kasthack.telemetry.dbconnection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace kasthack.telemetry.dbconnection.di;

/// <summary>
/// Extension methods for registering kasthack.telemetry.dbconnection services with <see cref="IServiceCollection"/>.
/// </summary>
public static class TelemetryDbConnectionServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="TelemetryDbConnectionFactory"/> as a singleton, configured from
    /// <see cref="IOptions{TOptions}"/> and an optional <see cref="ILogger{TCategoryName}"/>.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configure">
    /// An optional action to configure <see cref="TelemetryDbConnectionOptions"/> at registration time.
    /// Options can also be configured later via the standard <c>IOptions</c> pipeline.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddTelemetryDbConnection(
        this IServiceCollection services,
        Action<TelemetryDbConnectionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<TelemetryDbConnectionOptions>();

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddSingleton(static sp => new TelemetryDbConnectionFactory(
            // IOptions<T>.Value is read once here; for live option reloading use IOptionsMonitor<T>.CurrentValue instead.
            sp.GetRequiredService<IOptions<TelemetryDbConnectionOptions>>().Value,
            sp.GetService<ILogger<TelemetryDbConnectionFactory>>()));

        return services;
    }
}
