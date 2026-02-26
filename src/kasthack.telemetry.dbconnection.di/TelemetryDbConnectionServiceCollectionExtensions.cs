using kasthack.telemetry.dbconnection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Data.Common;

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

    /// <summary>
    /// Registers <see cref="TelemetryDbConnectionFactory"/> as a singleton and registers a scoped
    /// <see cref="DbConnection"/> that is created by <paramref name="connectionFactory"/> and
    /// automatically wrapped with telemetry by the factory.
    /// Inject <see cref="DbConnection"/> directly in your services instead of calling
    /// <see cref="TelemetryDbConnectionFactory.Wrap"/> manually.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="connectionFactory">
    /// A delegate that creates the raw (unwrapped) <see cref="DbConnection"/> for each scope.
    /// The resulting connection is wrapped automatically before being returned to the consumer.
    /// </param>
    /// <param name="configure">
    /// An optional action to configure <see cref="TelemetryDbConnectionOptions"/> at registration time.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddTelemetryDbConnection(
        this IServiceCollection services,
        Func<IServiceProvider, DbConnection> connectionFactory,
        Action<TelemetryDbConnectionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connectionFactory);

        services.AddTelemetryDbConnection(configure);

        services.AddScoped<DbConnection>(sp =>
            sp.GetRequiredService<TelemetryDbConnectionFactory>().Wrap(connectionFactory(sp)));

        return services;
    }
}
