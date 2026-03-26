using kasthack.telemetry.dbconnection.Options;

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
    // ── Non-keyed overloads ──────────────────────────────────────────────────

    /// <summary>
    /// Registers <see cref="TelemetryDbConnectionFactory"/> as a singleton, configured from
    /// <see cref="IOptionsMonitor{TOptions}"/> and an optional <see cref="ILogger{TCategoryName}"/>.
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
        RegisterFactory(services, serviceKey: null, configure);
        return services;
    }

    /// <summary>
    /// Registers <see cref="TelemetryDbConnectionFactory"/> as a singleton and registers a
    /// <see cref="DbConnection"/> that is created by <paramref name="connectionFactory"/> and
    /// automatically wrapped with telemetry.
    /// Inject <see cref="DbConnection"/> directly in your services instead of calling
    /// <see cref="TelemetryDbConnectionFactory.Wrap"/> manually.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="connectionFactory">
    /// A delegate that creates the raw (unwrapped) <see cref="DbConnection"/> for each resolution.
    /// The resulting connection is wrapped automatically before being returned to the consumer.
    /// </param>
    /// <param name="configure">
    /// An optional action to configure <see cref="TelemetryDbConnectionOptions"/> at registration time.
    /// </param>
    /// <param name="connectionLifetime">
    /// The <see cref="ServiceLifetime"/> for the registered <see cref="DbConnection"/>.
    /// Defaults to <see cref="ServiceLifetime.Scoped"/>.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddTelemetryDbConnection(
        this IServiceCollection services,
        Func<IServiceProvider, DbConnection> connectionFactory,
        Action<TelemetryDbConnectionOptions>? configure = null,
        ServiceLifetime connectionLifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connectionFactory);
        RegisterFactory(services, serviceKey: null, configure);
        RegisterConnection(services, serviceKey: null, connectionFactory, connectionLifetime);
        return services;
    }

    // ── Keyed overloads ──────────────────────────────────────────────────────

    /// <summary>
    /// Registers <see cref="TelemetryDbConnectionFactory"/> as a keyed singleton under
    /// <paramref name="serviceKey"/>, enabling multiple independent database configurations
    /// to coexist in the same container.
    /// Resolve with <c>[FromKeyedServices(key)]</c> or
    /// <c>IKeyedServiceProvider.GetRequiredKeyedService&lt;TelemetryDbConnectionFactory&gt;(key)</c>.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="serviceKey">The key used to identify this registration.</param>
    /// <param name="configure">
    /// An optional action to configure <see cref="TelemetryDbConnectionOptions"/> for this key.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddTelemetryDbConnection(
        this IServiceCollection services,
        object serviceKey,
        Action<TelemetryDbConnectionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(serviceKey);
        RegisterFactory(services, serviceKey, configure);
        return services;
    }

    /// <summary>
    /// Registers <see cref="TelemetryDbConnectionFactory"/> as a keyed singleton and registers a
    /// keyed <see cref="DbConnection"/> that is created by <paramref name="connectionFactory"/> and
    /// automatically wrapped with telemetry.
    /// Inject the keyed <see cref="DbConnection"/> with <c>[FromKeyedServices(key)]</c>.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="serviceKey">The key used to identify this registration.</param>
    /// <param name="connectionFactory">
    /// A delegate that creates the raw (unwrapped) <see cref="DbConnection"/> for each resolution.
    /// The resulting connection is wrapped automatically before being returned to the consumer.
    /// </param>
    /// <param name="configure">
    /// An optional action to configure <see cref="TelemetryDbConnectionOptions"/> for this key.
    /// </param>
    /// <param name="connectionLifetime">
    /// The <see cref="ServiceLifetime"/> for the registered <see cref="DbConnection"/>.
    /// Defaults to <see cref="ServiceLifetime.Scoped"/>.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddTelemetryDbConnection(
        this IServiceCollection services,
        object serviceKey,
        Func<IServiceProvider, DbConnection> connectionFactory,
        Action<TelemetryDbConnectionOptions>? configure = null,
        ServiceLifetime connectionLifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(serviceKey);
        ArgumentNullException.ThrowIfNull(connectionFactory);
        RegisterFactory(services, serviceKey, configure);
        RegisterConnection(services, serviceKey, connectionFactory, connectionLifetime);
        return services;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static void RegisterFactory(
        IServiceCollection services,
        object? serviceKey,
        Action<TelemetryDbConnectionOptions>? configure)
    {
        var optionsName = serviceKey?.ToString() ?? Microsoft.Extensions.Options.Options.DefaultName;

        services.AddOptions<TelemetryDbConnectionOptions>(optionsName);

        if (configure is not null)
        {
            services.Configure(optionsName, configure);
        }

        if (serviceKey is null)
        {
            services.AddSingleton(sp => CreateFactory(sp, optionsName));
        }
        else
        {
            services.AddKeyedSingleton<TelemetryDbConnectionFactory>(
                serviceKey,
                (sp, _) => CreateFactory(sp, optionsName));
        }
    }

    private static void RegisterConnection(
        IServiceCollection services,
        object? serviceKey,
        Func<IServiceProvider, DbConnection> rawConnectionFactory,
        ServiceLifetime lifetime)
    {
        if (serviceKey is null)
        {
            AddWithLifetime<DbConnection>(
                services,
                lifetime,
                sp => sp.GetRequiredService<TelemetryDbConnectionFactory>().Wrap(rawConnectionFactory(sp)));
        }
        else
        {
            AddKeyedWithLifetime<DbConnection>(
                services,
                serviceKey,
                lifetime,
                (sp, key) => sp.GetRequiredKeyedService<TelemetryDbConnectionFactory>(key).Wrap(rawConnectionFactory(sp)));
        }
    }

    private static TelemetryDbConnectionFactory CreateFactory(IServiceProvider sp, string optionsName) =>
        new(
            sp.GetRequiredService<IOptionsMonitor<TelemetryDbConnectionOptions>>().Get(optionsName),
            sp.GetService<ILogger<TelemetryDbConnectionFactory>>());

    private static void AddWithLifetime<TService>(
        IServiceCollection services,
        ServiceLifetime lifetime,
        Func<IServiceProvider, TService> factory)
        where TService : class
    {
        _ = lifetime switch
        {
            ServiceLifetime.Singleton => services.AddSingleton(factory),
            ServiceLifetime.Scoped    => services.AddScoped(factory),
            ServiceLifetime.Transient => services.AddTransient(factory),
            _ => throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, null),
        };
    }

    private static void AddKeyedWithLifetime<TService>(
        IServiceCollection services,
        object serviceKey,
        ServiceLifetime lifetime,
        Func<IServiceProvider, object?, TService> factory)
        where TService : class
    {
        _ = lifetime switch
        {
            ServiceLifetime.Singleton => services.AddKeyedSingleton(serviceKey, factory),
            ServiceLifetime.Scoped    => services.AddKeyedScoped(serviceKey, factory),
            ServiceLifetime.Transient => services.AddKeyedTransient(serviceKey, factory),
            _ => throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, null),
        };
    }
}

