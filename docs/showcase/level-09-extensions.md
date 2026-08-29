<!-- Copyright © Erickson Lopez. MIT License. -->

# Level 9: Framework Integrations

This level explores the official framework integration packages: Entity Framework Core and MassTransit.

## 1. Entity Framework Core Integration

Package: `EricksonLopez.Outbox.EntityFrameworkCore`

### Setup

```csharp
// 1. Apply model configurations in your DbContext
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);

    // Registers OutboxMessageEntity, IdempotencyRecordEntity, DeadLetterMessageEntity.
    // Accepts an optional schema name (default: "outbox"):
    modelBuilder.ApplyOutboxEntityConfigurations(schema: "outbox");
}

// 2. Register EF Core outbox repositories
builder.Services.AddOutboxEntityFrameworkCore<AppDbContext>();
```

### What `AddOutboxEntityFrameworkCore<T>()` Registers

| Service | Implementation | Lifetime |
|---|---|---|
| `IOutboxRepository` | `EntityFrameworkCoreOutboxRepository<TDbContext>` | Scoped |
| `IIdempotencyRepository` | `EntityFrameworkCoreIdempotencyRepository<TDbContext>` | Scoped |
| `IDeadLetterRepository` | `EntityFrameworkCoreDeadLetterRepository<TDbContext>` | Singleton |
| `IOutbox` | `DefaultOutbox` (default implementation) | Scoped |

> [!NOTE]
> `AddOutboxEntityFrameworkCore<T>()` uses `TryAddScoped` / `TryAddSingleton` — it never overwrites a previously registered service. Register custom implementations **before** calling this method if you need to override any of the above.

### What `ApplyOutboxEntityConfigurations()` Creates

| Table | EF Core Entity | Purpose |
|---|---|---|
| `{schema}.messages` | `OutboxMessageEntity` | Outbox message storage. |
| `{schema}.dead_letters` | `DeadLetterMessageEntity` | Failed messages after max retries. |
| `{schema}.idempotency` | `IdempotencyRecordEntity` | Inbox deduplication records. |

### Column Mappings (Key Columns)

| Column | C# Property | Notes |
|---|---|---|
| `id` | `Id` (Guid) | `ValueGeneratedNever()` — set by `IOutbox`. |
| `type` | `MessageType` | Max 255 chars. Stores the `[OutboxMessage]` alias. |
| `payload` | `Payload` | Binary/JSONB. The serialized message body. |
| `correlation_id` | `CorrelationId` | Max 255 chars. Nullable. |
| `causation_id` | `CausationId` | Max 255 chars. Nullable. |
| `headers_json` | `HeadersJson` | Default: `{}`. Custom headers as JSON. |
| `created_at` | `CreatedAt` | UTC timestamp. Set by `IOutbox`. |
| `deliver_at` | `DeliverAt` | Nullable. Scheduled dispatch timestamp. |
| `state` | `State` (`OutboxMessageStatus`) | `0`=Pending, `1`=InFlight, `3`=Failed, `4`=DeadLettered. |
| `retry_count` | `RetryCount` | Incremented on each failed dispatch attempt. |
| `error` | `Error` | Last error message (nullable, max varies). |

### Generating a Migration

After calling `ApplyOutboxEntityConfigurations()` in `OnModelCreating`, generate the migration:

```bash
dotnet ef migrations add AddOutboxTables --project YourProject
dotnet ef database update
```

### Using EF Core Transactions

With EF Core, extract the underlying `DbTransaction` using `GetDbTransaction()`:

```csharp
public async Task CreateOrderAsync(Order order, CancellationToken ct)
{
    await using var tx = await _db.Database.BeginTransactionAsync(ct);

    // Option A: explicit constructor
    var txContext = new DbTransactionContext(tx.GetDbTransaction());

    // Option B: extension method shorthand (from OutboxTransactionContextExtensions)
    // var txContext = tx.GetDbTransaction().ToOutboxContext();

    _db.Orders.Add(order);
    await _outbox.StoreAsync(new OrderCreatedEvent(order.Id), txContext, ct);

    await _db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
}
```

> [!NOTE]
> Use `tx.GetDbTransaction()` to extract the raw `DbTransaction` from EF Core's
> `IDbContextTransaction`. The `DbTransactionContext` wraps it for the outbox.
> The `.ToOutboxContext()` extension is defined in `OutboxTransactionContextExtensions`.

### Transaction Context Types

| Type | Namespace | Use Case |
|---|---|---|
| `DbTransactionContext` | `EricksonLopez.Outbox.Persistence` | Wraps any ADO.NET `DbTransaction`. Use with EF Core or raw ADO.NET. |
| `IOutboxTransactionContext` | `EricksonLopez.Outbox.Persistence` | Base interface. Implement for custom storage engines. |
| `IRelationalOutboxTransactionContext` | `EricksonLopez.Outbox.Persistence` | Extended interface that exposes the underlying `DbTransaction` for repositories that need it. `DbTransactionContext` implements this. |

---

## 2. MassTransit Integration

Package: `EricksonLopez.Outbox.MassTransit`

### `MassTransitBrokerPublisher`

This adapter implements `ITypedBrokerPublisher` and delegates to MassTransit's `IPublishEndpoint`:

```csharp
using EricksonLopez.Outbox.MassTransit;

builder.Services.AddOutbox(options =>
{
    options.UseBroker(sp => new MassTransitBrokerPublisher(
        sp.GetRequiredService<IPublishEndpoint>(),
        sp.GetRequiredService<IOutboxMessageTypeResolver>(),
        sp.GetRequiredService<IOutboxSerializer>()));
});
```

### `InboxIdempotencyFilter`

A MassTransit consume filter that provides automatic idempotency for all consumers:

```csharp
builder.Services.AddMassTransit(cfg =>
{
    cfg.UsingRabbitMq((context, rabbit) =>
    {
        // Register the idempotency filter globally — applies to all consumer endpoints
        rabbit.UseConsumeFilter(typeof(InboxIdempotencyFilter<>), context);
        rabbit.ConfigureEndpoints(context);
    });
});
```

The filter intercepts every consumed message:
1. Extracts the `MessageId` from the MassTransit message envelope
2. Calls `IInboxIdempotencyChecker.ShouldSkipAsync()` with the message ID
3. If the message was already processed, calls `context.Discard()` to skip it silently
4. Otherwise, allows processing to continue

---

## 3. Raw ADO.NET Storage Providers

For maximum performance and NativeAOT compatibility, use the raw storage providers.

### Provider Registration Pattern

Each provider requires the database-specific connection pool object (not a connection string):

```csharp
using EricksonLopez.Outbox.Persistence;
```

#### PostgreSQL

```csharp
using EricksonLopez.Outbox.Storage.PostgreSql;
using Npgsql;

// The provider requires NpgsqlDataSource — NOT a raw connection string.
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));

builder.Services.AddScoped<IOutboxRepository, PostgreSqlOutboxRepository>();
builder.Services.AddScoped<IIdempotencyRepository, PostgreSqlIdempotencyRepository>();
builder.Services.AddScoped<IDeadLetterRepository, PostgreSqlDeadLetterRepository>();
```

#### SQL Server

```csharp
using EricksonLopez.Outbox.Storage.SqlServer;
using Microsoft.Data.SqlClient;

builder.Services.AddSingleton(_ => new SqlConnection(connectionString));

builder.Services.AddScoped<IOutboxRepository, SqlServerOutboxRepository>();
builder.Services.AddScoped<IIdempotencyRepository, SqlServerIdempotencyRepository>();
builder.Services.AddScoped<IDeadLetterRepository, SqlServerDeadLetterRepository>();
```

#### MySQL

```csharp
using EricksonLopez.Outbox.Storage.MySql;
using MySqlConnector;

builder.Services.AddSingleton(_ => new MySqlDataSource(connectionString));

builder.Services.AddScoped<IOutboxRepository, MySqlOutboxRepository>();
builder.Services.AddScoped<IIdempotencyRepository, MySqlIdempotencyRepository>();
builder.Services.AddScoped<IDeadLetterRepository, MySqlDeadLetterRepository>();
```

### Provider Comparison

| Feature | EF Core | Raw ADO.NET |
|---|---|---|
| **Developer Experience** | Excellent — DbContext integration, code-first migrations | Requires manual schema setup |
| **Performance** | Very good — slight EF overhead | Maximum — zero allocation hot paths |
| **NativeAOT** | ⚠️ Limited (EF uses reflection) | ✅ Fully compatible |
| **Multi-TFM** | `net8.0`, `net9.0`, `net10.0` | `net8.0`, `net9.0`, `net10.0` |
| **Schema Customization** | Via `ApplyOutboxEntityConfigurations(schema)` | Via `OutboxRuntimeOptions.SchemaName` |

---

## 4. Manual Outbox Dispatcher (`ManualOutboxDispatcher`)

For serverless environments (Azure Functions, AWS Lambda) or scenarios where dispatching must be triggered on-demand, use `ManualOutboxDispatcher` instead of the background service.

> [!NOTE]
> `ManualOutboxDispatcher` is **not** registered by `AddOutbox()` or `AddOutboxDispatcher()`. You must register it manually.

```csharp
// Registration
builder.Services.AddScoped<ManualOutboxDispatcher>();

// Do NOT call AddOutboxDispatcher() in serverless — there is no background host.
```

```csharp
// Usage in an API endpoint / Azure Function trigger:
using EricksonLopez.Outbox.Hosting;
using EricksonLopez.Outbox.Persistence;

app.MapPost("/admin/dispatch", async (
    ManualOutboxDispatcher dispatcher,
    IOutboxRepository repository,
    CancellationToken ct) =>
{
    int dispatched = await dispatcher.DispatchPendingAsync(
        repository,
        batchSize: 50,  // Max messages to fetch and dispatch in this invocation
        ct);

    return Results.Ok(new { dispatched });
});
```

### `ManualOutboxDispatcher.DispatchPendingAsync()` API

| Parameter | Description |
|---|---|
| `IOutboxRepository repository` | The repository from which to fetch pending messages. |
| `int batchSize` | Maximum messages to dispatch in this call (default: `50`). |
| `CancellationToken cancellationToken` | Cancellation token. |
| **Returns** | `Task<int>` — the number of messages successfully dispatched. |

---

**Next:** In [Level 10](level-10-enterprise-architecture.md), you will learn about enterprise deployment patterns, testing strategies, and production hardening.
