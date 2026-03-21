using kasthack.telemetry.dbconnection.Decorators;
using kasthack.telemetry.dbconnection.di;
using kasthack.telemetry.dbconnection.Options;

using Microsoft.Extensions.DependencyInjection;

using System.Data.Common;

using Xunit;

namespace kasthack.telemetry.dbconnection.tests;

public sealed class TelemetryDbConnectionDiTests
{
    // ── Non-keyed, factory-only ──────────────────────────────────────────────

    [Fact]
    public void AddTelemetryDbConnectionRegistersFactoryAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddTelemetryDbConnection();

        using var sp = services.BuildServiceProvider();
        var factory1 = sp.GetRequiredService<TelemetryDbConnectionFactory>();
        var factory2 = sp.GetRequiredService<TelemetryDbConnectionFactory>();

        Assert.Same(factory1, factory2);
    }

    [Fact]
    public void AddTelemetryDbConnectionConfigureAppliesOptions()
    {
        var services = new ServiceCollection();
        services.AddTelemetryDbConnection(o => o.CaptureStatements = CaptureStatements.All);

        using var sp = services.BuildServiceProvider();
        // Smoke-test: factory resolves and works (options are read at factory creation time)
        var factory = sp.GetRequiredService<TelemetryDbConnectionFactory>();
        Assert.NotNull(factory);
    }

    // ── Non-keyed, connection-factory ────────────────────────────────────────

    [Fact]
    public void AddTelemetryDbConnectionWithConnectionFactoryRegistersDbConnection()
    {
        var services = new ServiceCollection();
        services.AddTelemetryDbConnection(_ => new MockDbConnection());

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var conn = scope.ServiceProvider.GetRequiredService<DbConnection>();

        Assert.IsType<TelemetryDbConnection>(conn);
    }

    [Fact]
    public void AddTelemetryDbConnectionWithConnectionFactoryScopedReturnsDifferentInstancePerScope()
    {
        var services = new ServiceCollection();
        services.AddTelemetryDbConnection(
            _ => new MockDbConnection(),
            connectionLifetime: ServiceLifetime.Scoped);

        using var sp = services.BuildServiceProvider();

        DbConnection conn1;
        using (var scope1 = sp.CreateScope())
        {
            conn1 = scope1.ServiceProvider.GetRequiredService<DbConnection>();
        }

        DbConnection conn2;
        using (var scope2 = sp.CreateScope())
        {
            conn2 = scope2.ServiceProvider.GetRequiredService<DbConnection>();
        }

        Assert.NotSame(conn1, conn2);
    }

    [Fact]
    public void AddTelemetryDbConnectionWithConnectionFactoryTransientReturnsDifferentInstanceEachTime()
    {
        var services = new ServiceCollection();
        services.AddTelemetryDbConnection(
            _ => new MockDbConnection(),
            connectionLifetime: ServiceLifetime.Transient);

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var conn1 = scope.ServiceProvider.GetRequiredService<DbConnection>();
        var conn2 = scope.ServiceProvider.GetRequiredService<DbConnection>();

        Assert.NotSame(conn1, conn2);
    }

    [Fact]
    public void AddTelemetryDbConnectionWithConnectionFactorySingletonReturnsSameInstance()
    {
        var services = new ServiceCollection();
        services.AddTelemetryDbConnection(
            _ => new MockDbConnection(),
            connectionLifetime: ServiceLifetime.Singleton);

        using var sp = services.BuildServiceProvider();
        var conn1 = sp.GetRequiredService<DbConnection>();
        var conn2 = sp.GetRequiredService<DbConnection>();

        Assert.Same(conn1, conn2);
    }

    // ── Keyed, factory-only ──────────────────────────────────────────────────

    [Fact]
    public void AddTelemetryDbConnectionKeyedRegistersKeyedFactory()
    {
        var services = new ServiceCollection();
        services.AddTelemetryDbConnection("db1");

        using var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredKeyedService<TelemetryDbConnectionFactory>("db1");

        Assert.NotNull(factory);
    }

    [Fact]
    public void AddTelemetryDbConnectionTwoKeysRegisterIndependentFactories()
    {
        var services = new ServiceCollection();
        services.AddTelemetryDbConnection("db1", o => o.EmitTraces = true);
        services.AddTelemetryDbConnection("db2", o => o.EmitTraces = false);

        using var sp = services.BuildServiceProvider();
        var factory1 = sp.GetRequiredKeyedService<TelemetryDbConnectionFactory>("db1");
        var factory2 = sp.GetRequiredKeyedService<TelemetryDbConnectionFactory>("db2");

        Assert.NotSame(factory1, factory2);
    }

    [Fact]
    public void AddTelemetryDbConnectionKeyedAndNonKeyedDoNotConflict()
    {
        var services = new ServiceCollection();
        services.AddTelemetryDbConnection();          // non-keyed
        services.AddTelemetryDbConnection("db1");     // keyed

        using var sp = services.BuildServiceProvider();
        var unkeyed = sp.GetRequiredService<TelemetryDbConnectionFactory>();
        var keyed   = sp.GetRequiredKeyedService<TelemetryDbConnectionFactory>("db1");

        Assert.NotSame(unkeyed, keyed);
    }

    // ── Keyed, connection-factory ────────────────────────────────────────────

    [Fact]
    public void AddTelemetryDbConnectionKeyedWithConnectionFactoryRegistersKeyedDbConnection()
    {
        var services = new ServiceCollection();
        services.AddTelemetryDbConnection("db1", _ => new MockDbConnection());

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var conn = scope.ServiceProvider.GetRequiredKeyedService<DbConnection>("db1");

        Assert.IsType<TelemetryDbConnection>(conn);
    }

    [Fact]
    public void AddTelemetryDbConnectionTwoKeyedConnectionsAreIndependent()
    {
        var services = new ServiceCollection();
        services.AddTelemetryDbConnection("db1", _ => new MockDbConnection());
        services.AddTelemetryDbConnection("db2", _ => new MockDbConnection());

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var conn1 = scope.ServiceProvider.GetRequiredKeyedService<DbConnection>("db1");
        var conn2 = scope.ServiceProvider.GetRequiredKeyedService<DbConnection>("db2");

        Assert.IsType<TelemetryDbConnection>(conn1);
        Assert.IsType<TelemetryDbConnection>(conn2);
        Assert.NotSame(conn1, conn2);
    }

    [Fact]
    public void AddTelemetryDbConnectionKeyedTransientReturnsDifferentInstanceEachTime()
    {
        var services = new ServiceCollection();
        services.AddTelemetryDbConnection(
            "db1",
            _ => new MockDbConnection(),
            connectionLifetime: ServiceLifetime.Transient);

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var conn1 = scope.ServiceProvider.GetRequiredKeyedService<DbConnection>("db1");
        var conn2 = scope.ServiceProvider.GetRequiredKeyedService<DbConnection>("db1");

        Assert.NotSame(conn1, conn2);
    }

    [Fact]
    public void AddTelemetryDbConnectionKeyedSingletonReturnsSameInstance()
    {
        var services = new ServiceCollection();
        services.AddTelemetryDbConnection(
            "db1",
            _ => new MockDbConnection(),
            connectionLifetime: ServiceLifetime.Singleton);

        using var sp = services.BuildServiceProvider();
        var conn1 = sp.GetRequiredKeyedService<DbConnection>("db1");
        var conn2 = sp.GetRequiredKeyedService<DbConnection>("db1");

        Assert.Same(conn1, conn2);
    }
}
