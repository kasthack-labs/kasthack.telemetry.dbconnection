# kasthack.telemetry.dbconnection

[![Github All Releases](https://img.shields.io/github/downloads/kasthack-labs/kasthack.telemetry.dbconnection/total.svg)](https://github.com/kasthack-labs/kasthack.telemetry.dbconnection/releases/latest)
[![GitHub release](https://img.shields.io/github/release/kasthack-labs/kasthack.telemetry.dbconnection.svg)](https://github.com/kasthack-labs/kasthack.telemetry.dbconnection/releases/latest)
[![license](https://img.shields.io/github/license/kasthack-labs/kasthack.telemetry.dbconnection.svg)](LICENSE)
[![.NET Status](https://github.com/kasthack-labs/kasthack.telemetry.dbconnection/workflows/.NET/badge.svg)](https://github.com/kasthack-labs/kasthack.telemetry.dbconnection/actions?query=workflow%3A.NET)
[![NuGet](https://img.shields.io/nuget/v/kasthack.telemetry.dbconnection.svg)](https://www.nuget.org/packages/kasthack.telemetry.dbconnection/)
[![NuGet EF](https://img.shields.io/nuget/v/kasthack.telemetry.dbconnection.ef.svg)](https://www.nuget.org/packages/kasthack.telemetry.dbconnection.ef/)
[![NuGet DI](https://img.shields.io/nuget/v/kasthack.telemetry.dbconnection.di.svg)](https://www.nuget.org/packages/kasthack.telemetry.dbconnection.di/)
[![Patreon pledges](https://img.shields.io/endpoint.svg?url=https%3A%2F%2Fshieldsio-patreon.vercel.app%2Fapi%3Fusername%3Dkasthack%26type%3Dpledges&style=flat)](https://patreon.com/kasthack)
[![Patreon patrons](https://img.shields.io/endpoint.svg?url=https%3A%2F%2Fshieldsio-patreon.vercel.app%2Fapi%3Fusername%3Dkasthack%26type%3Dpatrons&style=flat)](https://patreon.com/kasthack)

## What

A set of .NET NuGet packages that wrap any `DbConnection` with OpenTelemetry-compatible **distributed traces** (`ActivitySource`) and **metrics** (`Meter` / `Histogram`), following the [OpenTelemetry database semantic conventions](https://opentelemetry.io/docs/specs/semconv/database/).

## Why does this exist?

- **Uniformity across drivers** — a single instrumentation layer works identically with SQLite, SQL Server, PostgreSQL, MySQL, or any other ADO.NET provider, without per-driver plugins; driver-specific built-in tracing (e.g., SqlClient) provides traces but **no metrics**.
- **.NET Framework / netstandard support** — targets `netstandard2.0` so it works in legacy .NET Framework applications as well as modern .NET.
- **Transaction instrumentation** — `Commit`, `Rollback`, `Save`/`Release` savepoints are all measured and traced, not just query execution.
- **Correct reader timing** — the span for `ExecuteReader` stays open until the `DbDataReader` is disposed, capturing the full time spent reading rows; built-in SqlClient tracing closes the span at execute time and misses reader duration.

### Packages

| Package | Description |
|---|---|
| `kasthack.telemetry.dbconnection` | Core ADO.NET wrapper |
| `kasthack.telemetry.dbconnection.ef` | Entity Framework Core interceptor |
| `kasthack.telemetry.dbconnection.di` | `IServiceCollection` / `IOptions` integration |

## What is instrumented

- **Connection open** (`Open` / `OpenAsync`) — separate named operation `connect`
- **Command execution** (`ExecuteNonQuery`, `ExecuteScalar`, `ExecuteReader`, `Prepare`, `Cancel` — sync and async) — operation name derived from the first SQL keyword (e.g. `SELECT`, `INSERT`); `Prepare` uses `prepare`, `Cancel` uses `cancel`
- **Reader lifetime** — for `ExecuteReader`, the span stays open until the `DbDataReader` is disposed
- **Transaction operations** (`Commit`, `Rollback`, `Save`/`Rollback`/`Release` savepoints — sync and async) — each produces its own span and metric
- Each execution of a reused command produces its own span and metric

### Tags (semantic conventions)

| Tag | Value |
|---|---|
| `db.name` | `DbConnection.Database` |
| `db.operation` | SQL verb or `connect` |
| `db.statement` | Full command text |
| `error.type` | Exception type name (on failure) |

### Metrics

| Instrument | Unit | Description |
|---|---|---|
| `db.client.operation.duration` | `s` | Histogram of database operation durations |

## Usage

### Core (plain ADO.NET)

```csharp
using kasthack.telemetry.dbconnection;

var factory = new TelemetryDbConnectionFactory(new TelemetryDbConnectionOptions
{
    EmitTraces  = true,
    EmitMetrics = true,
    // optionally enrich every activity with custom tags:
    EnrichActivity = (activity, conn) => activity.SetTag("app.tenant", tenantId),
    // optionally enrich every metric measurement with extra tags:
    EnrichMetrics  = (tags, conn) => tags.Add(new("app.tenant", tenantId)),
});

// Wrap a raw DbConnection — works with any ADO.NET provider
using var connection = factory.Wrap(new SqliteConnection("Data Source=:memory:"));
await connection.OpenAsync();
```

The `TelemetryDbConnection.InnerConnection` property exposes the underlying connection when needed.

### Register with OpenTelemetry SDK

```csharp
// Traces
tracerProviderBuilder.AddSource(TelemetryDbConnectionInstrumentation.ActivitySourceName);

// Metrics
meterProviderBuilder.AddMeter(TelemetryDbConnectionInstrumentation.MeterName);
```

---

### Entity Framework Core

The EF package intercepts `ConnectionCreated` to wrap the connection EF just built with a `TelemetryDbConnection`. All subsequent operations (open, commands, readers) are instrumented automatically.

```csharp
using kasthack.telemetry.dbconnection.ef;

// In DbContext configuration:
optionsBuilder.AddInterceptors(new TelemetryDbConnectionInterceptor(
    new TelemetryDbConnectionOptions { EmitTraces = true, EmitMetrics = true }
));
```

---

### Dependency Injection / IOptions

The DI package registers `TelemetryDbConnectionFactory` as a singleton, resolves options from `IOptionsMonitor<TelemetryDbConnectionOptions>`, and automatically wires up an `ILogger<TelemetryDbConnectionFactory>` if one is available.

**Option A — inject the factory and wrap connections manually:**

```csharp
using kasthack.telemetry.dbconnection.di;

builder.Services.AddTelemetryDbConnection(options =>
{
    options.EmitTraces  = true;
    options.EmitMetrics = true;
});

// Inject TelemetryDbConnectionFactory and call Wrap() where needed:
public class MyRepository(TelemetryDbConnectionFactory factory)
{
    public async Task<int> CountAsync()
    {
        using var conn = factory.Wrap(new SqliteConnection("Data Source=:memory:"));
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM MyTable";
        return (int)(await cmd.ExecuteScalarAsync())!;
    }
}
```

**Option B — register a `DbConnection` directly:**

Pass a connection factory delegate to get a `DbConnection` (already wrapped with telemetry) injected automatically. Use the `connectionLifetime` parameter to control the service lifetime (defaults to `Scoped`):

```csharp
using kasthack.telemetry.dbconnection.di;

builder.Services.AddTelemetryDbConnection(
    connectionFactory: _ => new SqliteConnection("Data Source=app.db"),
    configure: options =>
    {
        options.EmitTraces  = true;
        options.EmitMetrics = true;
    });
// connectionLifetime defaults to ServiceLifetime.Scoped

// Inject DbConnection directly — it is already wrapped with telemetry:
public class MyRepository(DbConnection conn) { ... }
```

**Option C — keyed services (multiple databases):**

Use the `serviceKey` overloads when you need multiple named database registrations in the same container. Keyed factories and connections are independent — each has its own `TelemetryDbConnectionOptions`.

```csharp
using kasthack.telemetry.dbconnection.di;

// Register two databases under different keys:
builder.Services.AddTelemetryDbConnection(
    serviceKey: "users-db",
    connectionFactory: _ => new SqliteConnection("Data Source=users.db"),
    configure: options => { options.EmitTraces = true; options.EmitMetrics = true; });

builder.Services.AddTelemetryDbConnection(
    serviceKey: "orders-db",
    connectionFactory: _ => new SqliteConnection("Data Source=orders.db"),
    configure: options => { options.EmitTraces = true; options.CaptureStatements = true; });

// Inject by key using [FromKeyedServices]:
public class UsersRepository([FromKeyedServices("users-db")] DbConnection conn) { ... }
public class OrdersRepository([FromKeyedServices("orders-db")] DbConnection conn) { ... }
```

Options can also be configured via the standard `IOptions` pipeline (e.g. `appsettings.json`, environment variables) after calling `AddTelemetryDbConnection`.

## Enrichment error handling

If an `EnrichActivity` or `EnrichMetrics` callback throws, the exception is **suppressed** (the operation continues normally) and a warning is written via `ILogger` when one is available.
