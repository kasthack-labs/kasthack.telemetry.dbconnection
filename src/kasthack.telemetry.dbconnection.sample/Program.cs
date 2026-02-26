/*
 * kasthack.telemetry.dbconnection — sample application
 *
 * Demonstrates all three packages against a SQLite in-memory database.
 * OTel traces and metrics are exported to the console so you can see the
 * output without standing up a collector.
 *
 * Sections
 *   1. Core  – plain ADO.NET via TelemetryDbConnectionFactory
 *   2. EF    – Entity Framework Core via TelemetryDbConnectionInterceptor
 *   3. DI    – IServiceCollection / IOptions via AddTelemetryDbConnection
 */

using kasthack.telemetry.dbconnection;
using kasthack.telemetry.dbconnection.di;
using kasthack.telemetry.dbconnection.ef;
using kasthack.telemetry.dbconnection.sample;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

// ── OTel SDK bootstrap ────────────────────────────────────────────────────────
// Build a TracerProvider and MeterProvider that listen to the library's
// ActivitySource / Meter and export everything to the console.

using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddSource(TelemetryDbConnectionInstrumentation.ActivitySourceName)
    .AddConsoleExporter()
    .Build();

using var meterProvider = Sdk.CreateMeterProviderBuilder()
    .AddMeter(TelemetryDbConnectionInstrumentation.MeterName)
    .AddConsoleExporter()
    .Build();

const string ConnectionString = "Data Source=:memory:";

// ════════════════════════════════════════════════════════════════════════════
// 1. Core — plain ADO.NET
// ════════════════════════════════════════════════════════════════════════════
Console.WriteLine("=== 1. Core (plain ADO.NET) ===");

var factory = new TelemetryDbConnectionFactory(new TelemetryDbConnectionOptions
{
    EmitTraces = true,
    EmitMetrics = true,
    CaptureStatements = true,
    // Enrich every span with a custom tag.
    EnrichActivity = (activity, cmd) =>
        activity.SetTag("sample.section", "core"),
    // Enrich every metric data point with a custom tag.
    EnrichMetrics = (tags, cmd) =>
        tags.Add(new KeyValuePair<string, object?>("sample.section", "core")),
});

using (var conn = factory.Wrap(new SqliteConnection(ConnectionString)))
{
    await conn.OpenAsync();

    // DDL
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = "CREATE TABLE products (id INTEGER PRIMARY KEY, name TEXT, price REAL)";
        await cmd.ExecuteNonQueryAsync();
    }

    // Insert
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = "INSERT INTO products (id, name, price) VALUES (1, 'Widget', 9.99)";
        await cmd.ExecuteNonQueryAsync();
    }

    // Scalar query
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = "SELECT COUNT(*) FROM products";
        var count = await cmd.ExecuteScalarAsync();
        Console.WriteLine($"  Row count: {count}");
    }

    // Reader query
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = "SELECT id, name, price FROM products";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            Console.WriteLine($"  Row: id={reader.GetInt32(0)} name={reader.GetString(1)} price={reader.GetDouble(2)}");
        }
    }
}

// ════════════════════════════════════════════════════════════════════════════
// 2. EF Core — TelemetryDbConnectionInterceptor
// ════════════════════════════════════════════════════════════════════════════
Console.WriteLine();
Console.WriteLine("=== 2. EF Core ===");

// Use a named in-memory database so all connections EF opens see the same
// schema and data.  A keepalive connection prevents the DB from being
// dropped while the context is in use.
const string EfConnectionString = "Data Source=ef_sample;Mode=Memory;Cache=Shared";
using var efKeepAlive = new SqliteConnection(EfConnectionString);
await efKeepAlive.OpenAsync();

// The interceptor wraps each newly created connection in a TelemetryDbConnection
// via ConnectionCreated; all telemetry is handled by the wrapper.
var interceptor = new TelemetryDbConnectionInterceptor(new TelemetryDbConnectionOptions
{
    EmitTraces = true,
    EmitMetrics = true,
    EnrichActivity = (activity, cmd) =>
        activity.SetTag("sample.section", "ef"),
});

var efOptions = new DbContextOptionsBuilder<SampleDbContext>()
    .UseSqlite(EfConnectionString)
    .AddInterceptors(interceptor)
    .Options;

using (var ctx = new SampleDbContext(efOptions))
{
    await ctx.Database.EnsureCreatedAsync();

    ctx.Products.Add(new Product { Id = 1, Name = "Gadget", Price = 19.99m });
    await ctx.SaveChangesAsync();

    var products = await ctx.Products.ToListAsync();
    Console.WriteLine($"  Products in EF context: {products.Count}");
    foreach (var p in products)
    {
        Console.WriteLine($"  Row: id={p.Id} name={p.Name} price={p.Price}");
    }
}

// ════════════════════════════════════════════════════════════════════════════
// 3. DI — IServiceCollection / IOptions / ILogger
// ════════════════════════════════════════════════════════════════════════════
Console.WriteLine();
Console.WriteLine("=== 3. DI / IOptions ===");

// Named in-memory DB so the keepalive connection and scoped connections share state.
const string DiConnectionString = "Data Source=di_sample;Mode=Memory;Cache=Shared";
using var diKeepAlive = new SqliteConnection(DiConnectionString);
await diKeepAlive.OpenAsync();

var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));

// Option B: register a scoped DbConnection (auto-wrapped with telemetry).
// Inject DbConnection directly — it is already wrapped.
services.AddTelemetryDbConnection(
    connectionFactory: _ => new SqliteConnection(DiConnectionString),
    configure: options =>
    {
        options.EmitTraces = true;
        options.EmitMetrics = true;
        options.EnrichActivity = (activity, cmd) =>
            activity.SetTag("sample.section", "di");
    });

using var sp = services.BuildServiceProvider();
using var scope = sp.CreateScope();
var diConn = scope.ServiceProvider.GetRequiredService<System.Data.Common.DbConnection>();
await diConn.OpenAsync();

using (var cmd = diConn.CreateCommand())
{
    cmd.CommandText = "CREATE TABLE IF NOT EXISTS items (id INTEGER PRIMARY KEY, label TEXT)";
    await cmd.ExecuteNonQueryAsync();
}

using (var cmd = diConn.CreateCommand())
{
    cmd.CommandText = "INSERT INTO items VALUES (1, 'hello'), (2, 'world')";
    await cmd.ExecuteNonQueryAsync();
}

using (var cmd = diConn.CreateCommand())
{
    cmd.CommandText = "SELECT COUNT(*) FROM items";
    var count = await cmd.ExecuteScalarAsync();
    Console.WriteLine($"  Item count: {count}");
}

Console.WriteLine();
Console.WriteLine("Done — check the console output above for OTel traces and metrics.");
