# kasthack.telemetry.dbconnection

[![Github All Releases](https://img.shields.io/github/downloads/kasthack-labs/kasthack.telemetry.dbconnection/total.svg)](https://github.com/kasthack-labs/kasthack.telemetry.dbconnection/releases/latest)
[![GitHub release](https://img.shields.io/github/release/kasthack-labs/kasthack.telemetry.dbconnection.svg)](https://github.com/kasthack-labs/kasthack.telemetry.dbconnection/releases/latest)
[![license](https://img.shields.io/github/license/kasthack-labs/kasthack.telemetry.dbconnection.svg)](LICENSE)
[![.NET Status](https://github.com/kasthack-labs/kasthack.telemetry.dbconnection/workflows/.NET/badge.svg)](https://github.com/kasthack-labs/kasthack.telemetry.dbconnection/actions?query=workflow%3A.NET)
[![Patreon pledges](https://img.shields.io/endpoint.svg?url=https%3A%2F%2Fshieldsio-patreon.vercel.app%2Fapi%3Fusername%3Dkasthack%26type%3Dpledges&style=flat)](https://patreon.com/kasthack)
[![Patreon patrons](https://img.shields.io/endpoint.svg?url=https%3A%2F%2Fshieldsio-patreon.vercel.app%2Fapi%3Fusername%3Dkasthack%26type%3Dpatrons&style=flat)](https://patreon.com/kasthack)

## What

A set of .NET NuGet packages that wrap any `DbConnection` with OpenTelemetry-compatible **distributed traces** (`ActivitySource`) and **metrics** (`Meter` / `Histogram`), following the [OpenTelemetry database semantic conventions](https://opentelemetry.io/docs/specs/semconv/database/).

### Packages

| Package | Description |
|---|---|
| `kasthack.telemetry.dbconnection` | Core ADO.NET wrapper |
| `kasthack.telemetry.dbconnection.ef` | Entity Framework Core interceptor |
| `kasthack.telemetry.dbconnection.di` | `IServiceCollection` / `IOptions` integration |

## What is instrumented

- **Connection open** (`Open` / `OpenAsync`) — separate named operation `connect`
- **Command execution** (`ExecuteNonQuery`, `ExecuteScalar`, `ExecuteReader` — sync and async) — operation name derived from the first SQL keyword (e.g. `SELECT`, `INSERT`)
- **Reader lifetime** — for `ExecuteReader`, the span stays open until the `DbDataReader` is disposed
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

The DI package registers `TelemetryDbConnectionFactory` as a singleton, resolves options from `IOptions<TelemetryDbConnectionOptions>`, and automatically wires up an `ILogger<TelemetryDbConnectionFactory>` if one is available.

```csharp
using kasthack.telemetry.dbconnection.di;

// In Program.cs / Startup.cs:
builder.Services.AddTelemetryDbConnection(options =>
{
    options.EmitTraces  = true;
    options.EmitMetrics = true;
});

// Later, inject TelemetryDbConnectionFactory where needed:
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

Options can also be configured via the standard `IOptions` pipeline (e.g. `appsettings.json`, environment variables) after calling `AddTelemetryDbConnection`.

## Enrichment error handling

If an `EnrichActivity` or `EnrichMetrics` callback throws, the exception is **suppressed** (the operation continues normally) and a warning is written via `ILogger` when one is available.
