# Level 2: Configuration

This level covers the full configuration surface of `EricksonLopez.Outbox` — serialization, broker routing, dispatcher tuning, runtime options, and raw ADO.NET setup without Entity Framework Core.

## 1. `AddOutbox()` — Core Configuration

The `AddOutbox()` extension method accepts an `Action<OutboxOptions>` delegate:

```csharp
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Hosting;
using EricksonLopez.Outbox.Serialization;
using EricksonLopez.Outbox.Generated; // Provided by EricksonLopez.Outbox.SourceGenerators

builder.Services.AddOutbox(options =>
{
    // --- Serialization (required) ---
    // NativeAotJsonSerializer uses a source-generated JsonSerializerContext for
    // zero-reflection, AOT-compatible serialization.
    options.UseSerializer(new NativeAotJsonSerializer(MyJsonContext.Default));

    // --- Type mapping ---
    // UseGeneratedTypes() is emitted by the EricksonLopez.Outbox.SourceGenerators package.
    // It registers an IOutboxMessageTypeResolver built from all [OutboxMessage(...)] types
    // discovered in your assembly at compile time — no reflection at runtime.
    options.UseGeneratedTypes();

    // --- OR: Manual type mapping (no source generators required) ---
    // options.UseTypeResolver(new InMemoryMessageTypeResolver(new[]
    // {
    //     ("order-created-v1", typeof(OrderCreatedEvent)),
    //     ("user-registered-v1", typeof(UserRegisteredEvent)),
    // }));

    // --- Default broker (used when no route matches) ---
    options.UseBroker(sp => new ConsoleBrokerPublisher());

    // --- Route-specific brokers (matched by message type alias) ---
    // Use options.Route(alias).ToPublisher(...) to send specific event types
    // to a dedicated broker publisher:
    options.Route("analytics-event-v1")
           .ToPublisher(sp => new KafkaBrokerPublisher(sp.GetRequiredService<IProducer<string, byte[]>>()));

    // Route to a pre-built publisher instance:
    options.Route("notification-sent-v1")
           .ToPublisher(new AwsSqsBrokerPublisher(queueUrl));
});
```

### `OutboxOptions` API Reference

| Method | Description |
|---|---|
| `UseSerializer(IOutboxSerializer)` | Sets the serializer **instance** (required; use `NativeAotJsonSerializer` for AOT). |
| `UseSerializer<TSerializer>()` | Registers a serializer **type** (resolved from DI). |
| `UseTypeResolver(IOutboxMessageTypeResolver)` | Sets the type resolver **instance** for alias → CLR type mapping. |
| `UseGeneratedTypes()` | Source-generator extension. Registers the compile-time type resolver (requires `EricksonLopez.Outbox.SourceGenerators`). |
| `UseBroker(Func<IServiceProvider, IBrokerPublisher>, RetryPolicy?, CircuitBreakerState?)` | Sets the **default** broker via factory. Optionally wraps with retry policy and/or circuit breaker. |
| `UseBroker(IBrokerPublisher, RetryPolicy?, CircuitBreakerState?)` | Sets the **default** broker via pre-built instance, with optional retry and circuit breaker. |
| `UseBroker<TBroker>(RetryPolicy?, CircuitBreakerState?)` | Sets the **default** broker by **type** (resolved from DI), with optional retry and circuit breaker. |
| `Route(string alias)` | Begins a **route-specific** broker configuration for the given message type alias. Returns a `BrokerRouteBuilder`. |
| `ConfigureRuntimeOptions(Action<OutboxRuntimeOptions>)` | Configures runtime behavior (table names, payload limits, etc.). |
| `Configure(Action<IServiceCollection>)` | **[Advanced]** Escape-hatch. Registers extra DI services directly. Avoid in end-user code; use for first-party integration libraries only. |

### `UseBroker()` with Retry Policy

All three `UseBroker()` overloads accept optional `retryPolicy` and `circuitBreaker` parameters. When a `RetryPolicy` is provided, the library wraps the publisher in a `RetryDispatcherInterceptor` automatically:

```csharp
using EricksonLopez.Outbox.Retry;

builder.Services.AddOutbox(options =>
{
    // Factory overload + retry policy + circuit breaker:
    options.UseBroker(
        factory: sp => new RabbitMqBrokerPublisher(sp.GetRequiredService<IConnection>()),
        retryPolicy: new ExponentialBackoffRetryPolicy(
            InitialDelay: TimeSpan.FromSeconds(1),
            MaxAttempts: 5,
            Factor: 2.0,
            MaxDelay: TimeSpan.FromSeconds(30)),
        circuitBreaker: new CircuitBreakerState(
            failureThreshold: 5,
            openDuration: TimeSpan.FromSeconds(30)));

    // Type overload + jitter policy (circuit breaker gets a default CircuitBreakerState(5)):
    options.UseBroker<KafkaBrokerPublisher>(
        retryPolicy: new JitterRetryPolicy(
            InitialDelay: TimeSpan.FromMilliseconds(500),
            MaxAttempts: 10));

    // Instance overload + retry only:
    options.UseBroker(
        publisher: new ConsoleBrokerPublisher(),
        retryPolicy: RetryPolicy.Default);
});
```

> [!NOTE]
> `RetryPolicy` controls the in-process delay between publish attempts within a single polling cycle.  
> `OutboxDispatcherOptions.MaxRetryCount` controls how many times a message can be re-fetched across polling cycles before being dead-lettered.  
> These are **two orthogonal mechanisms** — both can be active simultaneously.

### `OutboxOptions.BrokerRouteBuilder` API

Obtained via `options.Route("alias")`:

| Method | Description |
|---|---|
| `ToPublisher(IBrokerPublisher)` | Routes the alias to a pre-built publisher instance. |
| `ToPublisher(Func<IServiceProvider, IBrokerPublisher>)` | Routes the alias to a factory-resolved publisher. |

---

## 2. `AddOutboxDispatcher()` — Dispatcher Configuration

```csharp
builder.Services.AddOutboxDispatcher(options =>
{
    options.BatchSize = 100;                                   // Messages per polling cycle (default: 100)
    options.PollingInterval = TimeSpan.FromMilliseconds(500); // Poll interval when DB is empty (default: 500ms)
    options.UseAdaptivePolling = true;                         // Dynamic polling frequency (default: true)
    options.MaxDegreeOfParallelism = 4;                        // Concurrent dispatch workers (default: min(CPU, 8))
    options.ChannelCapacity = 1000;                            // In-memory channel back-pressure capacity (default: 1000)
    options.MaxBatchesPerSecond = 0;                           // 0 = no rate limit (default: 0)
    options.MaxRetryCount = 10;                                // Dead-letter after N failures (default: 10)
    options.ReclaimTimeout = TimeSpan.FromMinutes(5);          // Crash recovery timeout (default: 5 min)
    options.ReclaimInterval = TimeSpan.FromMinutes(1);         // How often to run reclaim (default: 1 min)
    options.ReclaimBatchLimit = 1000;                          // Max stale messages reclaimed per cycle (default: 1000)
    options.DbRetryMaxAttempts = 3;                            // DB operation retries (default: 3)
    options.DbRetryBaseDelayMs = 50;                           // DB retry base delay in ms (default: 50)
    options.PendingCountRefreshInterval = TimeSpan.FromSeconds(30); // OTel gauge refresh interval (default: 30s)
    options.HasOnlySingletonMiddlewares = false;               // Cache pipeline across batches if all singleton (default: false)
});
```

### `OutboxDispatcherOptions` Reference

| Property | Default | Description |
|---|---|---|
| `BatchSize` | `100` | Messages fetched per polling cycle. |
| `PollingInterval` | `500 ms` | Base interval between polls when the outbox is empty. |
| `UseAdaptivePolling` | `true` | Dynamically adjusts polling frequency based on throughput. |
| `MaxDegreeOfParallelism` | `min(CPU, 8)` | Concurrent dispatch workers. Reserved for v2.0 parallel impl. |
| `ChannelCapacity` | `1000` | Bounded channel capacity connecting the poller to consumers. |
| `MaxBatchesPerSecond` | `0` (unlimited) | Coarse-grained dispatcher rate limiter. |
| `MaxRetryCount` | `10` | Max dispatch attempts before moving a message to the Dead Letter Queue. |
| `ReclaimTimeout` | `5 min` | Duration after which an InFlight message is considered stale. |
| `ReclaimInterval` | `1 min` | Frequency of the stale-message reclaim background job. |
| `ReclaimBatchLimit` | `1000` | Max stale messages reclaimed per reclaim cycle. |
| `DbRetryMaxAttempts` | `3` | Retries for transient DB operations (e.g., `MarkAsDispatchedAsync`). |
| `DbRetryBaseDelayMs` | `50` | Base delay (ms) between DB retries, with exponential backoff. |
| `PendingCountRefreshInterval` | `30 s` | OpenTelemetry pending count gauge refresh interval. |
| `HasOnlySingletonMiddlewares` | `false` | When `true`, the pipeline is cached per batch (eliminates allocations). |

---

## 3. `ConfigureRuntimeOptions()` — Runtime Behavior

Runtime options control table layout, payload limits, and operational behavior:

```csharp
builder.Services.AddOutbox(options =>
{
    options.ConfigureRuntimeOptions(runtime =>
    {
        runtime.SchemaName = "outbox";           // DB schema (default: "outbox")
        runtime.TableName = "messages";          // Messages table name (default: "messages")
        runtime.MaxPayloadSizeInBytes = 1024 * 1024; // 1 MB payload limit (default: 1 MB)
        runtime.MaxHeaderSizeInBytes = 64 * 1024;    // 64 KB headers limit (default: 64 KB)
        runtime.MaxMessageAge = TimeSpan.FromDays(30); // Max message age / scheduling horizon (default: 30 days)
        runtime.MaxBackoffSeconds = 3600;        // Max backoff for failed messages (default: 3600 = 1h)
        runtime.DeleteOnDispatch = true;         // Delete row on success; false = UPDATE to state=2 (default: true)
        runtime.ThrowOnUnregisteredType = false; // Throw if message type alias is unregistered (default: false)
        runtime.LargeTableThreshold = 50_000;   // Row count above which PostgreSQL uses catalog estimates (default: 50k)
        runtime.MaxStoreRatePerSecond = 0;       // Max StoreAsync calls/s; 0 = unlimited (default: 0)
        runtime.IncludeMessageTypeTag = true;    // Include type tag on OTel metrics (false = reduce cardinality) (default: true)
        runtime.ReclaimBatchLimit = 1000;        // Stale message reclaim batch limit (default: 1000)
    });
});
```

### `OutboxRuntimeOptions` Reference

| Property | Default | Description |
|---|---|---|
| `InstanceId` | Auto GUID | Unique identifier for this dispatcher instance (auto-generated, read-only). |
| `SchemaName` | `"outbox"` | Database schema where outbox tables reside. |
| `TableName` | `"messages"` | Base name of the outbox messages table. |
| `MaxPayloadSizeInBytes` | `1 048 576` (1 MB) | Max serialized payload size. Exceeding it throws `OutboxPayloadTooLargeException`. |
| `MaxHeaderSizeInBytes` | `65 536` (64 KB) | Max serialized headers size. Exceeding it throws `OutboxHeadersTooLargeException`. |
| `MaxMessageAge` | `30 days` | Max message age. Also caps how far ahead `deliver_at` can be scheduled. |
| `MaxBackoffSeconds` | `3600` (1 h) | Cap on the SQL-computed exponential backoff for failed messages. |
| `DeleteOnDispatch` | `true` | `true` = DELETE dispatched rows (recommended). `false` = UPDATE to state=Reserved (audit mode). |
| `ThrowOnUnregisteredType` | `false` | Throw `OutboxTypeNotRegisteredException` if type alias is unknown. |
| `LargeTableThreshold` | `50 000` | Row estimate above which PostgreSQL uses `pg_class` estimates instead of COUNT(*). |
| `MaxStoreRatePerSecond` | `0` (unlimited) | Rate-limit on `StoreAsync`. Exceeding it throws `InvalidOperationException`. |
| `IncludeMessageTypeTag` | `true` | Include `messaging.message.type` dimension on OTel metrics. Set `false` to reduce cardinality. |
| `ReclaimBatchLimit` | `1000` | Max stale (InFlight) messages reset to Pending per reclaim cycle. |

> [!IMPORTANT]
> **`MaxMessageAge` and scheduled delivery**: If you schedule messages with `deliverAt = now + X days`, ensure `MaxMessageAge > X + 1 day`. Otherwise the `created_at` age guard in `FetchPendingAsync` will silently exclude the message and it will never be dispatched.

> [!WARNING]
> **`DeleteOnDispatch = false` (audit mode)**: Disabling delete-on-dispatch causes the outbox table to grow indefinitely. You **must** implement a periodic cleanup job:
> ```sql
> DELETE FROM outbox.messages WHERE state = 2 AND processed_at < NOW() - INTERVAL '7 days';
> ```

---

## 4. Raw ADO.NET Setup (Without Entity Framework Core)

For maximum performance and NativeAOT compatibility, use the raw Storage providers directly. Each provider requires a `NpgsqlDataSource` (or equivalent) rather than a plain connection string:

```csharp
using EricksonLopez.Outbox.Hosting;
using EricksonLopez.Outbox.Storage.PostgreSql;
using EricksonLopez.Outbox.Persistence;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")!;

// 1. Register NpgsqlDataSource (connection pool)
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));

// 2. Register raw ADO.NET repositories
builder.Services.AddScoped<IOutboxRepository, PostgreSqlOutboxRepository>();
builder.Services.AddScoped<IDeadLetterRepository, PostgreSqlDeadLetterRepository>();
builder.Services.AddScoped<IIdempotencyRepository, PostgreSqlIdempotencyRepository>();

// 3. Register Outbox core + dispatcher
builder.Services.AddOutbox(options =>
{
    options.UseSerializer(new NativeAotJsonSerializer(MyJsonContext.Default));
    options.UseGeneratedTypes();
    options.UseBroker(sp => new ConsoleBrokerPublisher());
});
builder.Services.AddOutboxDispatcher();
```

### Available Storage Providers

| Package | Database | ADO.NET Driver |
|---|---|---|
| `EricksonLopez.Outbox.Storage.PostgreSql` | PostgreSQL | `Npgsql` (requires `NpgsqlDataSource`) |
| `EricksonLopez.Outbox.Storage.SqlServer` | SQL Server | `Microsoft.Data.SqlClient` |
| `EricksonLopez.Outbox.Storage.MySql` | MySQL 8.0+ | `MySqlConnector` |
| `EricksonLopez.Outbox.Storage.Oracle` | Oracle 12c+ | `Oracle.ManagedDataAccess.Core` |
| `EricksonLopez.Outbox.Storage.Sqlite` | SQLite | `Microsoft.Data.Sqlite` (single instance only) |

---

## 5. Inbox Configuration

Register the Inbox for idempotent message consumption:

```csharp
builder.Services.AddOutboxInbox(options =>
{
    options.RetentionPeriod = TimeSpan.FromDays(7);            // How long processed records are kept (default: 7 days)
    options.DuplicateDetectionWindow = TimeSpan.FromHours(24); // Deduplication window (default: 24 hours)
    options.CleanupInterval = TimeSpan.FromHours(1);           // Background cleanup frequency (default: 1 hour)
});
```

### `OutboxInboxOptions` Reference

| Property | Default | Description |
|---|---|---|
| `RetentionPeriod` | `7 days` | How long idempotency records are retained before being purged. |
| `DuplicateDetectionWindow` | `24 hours` | Window within which duplicate messages are detected. |
| `CleanupInterval` | `1 hour` | Interval between `InboxCleanupService` background runs. |

---

## 6. Health Checks

```csharp
builder.Services.AddHealthChecks()
    .AddOutbox(
        name: "outbox",                // Health check entry name (default: "outbox")
        warningThreshold: 1000,        // Degraded if pending messages exceed this value
        tags: ["ready", "liveness"]); // Optional tags for health check filtering
```

### `OutboxHealthCheckOptions` Reference

| Property | Default | Description |
|---|---|---|
| `WarningThreshold` | `1000` | Pending message count above which the health check reports `Degraded`. |

| Health Status | Condition |
|---|---|
| `Healthy` | Dispatcher running, pending messages below `WarningThreshold`. |
| `Degraded` | Pending messages exceed `WarningThreshold`. |
| `Unhealthy` | Dispatcher not running, or repository is unreachable. |

---

**Next:** In [Level 3](level-03-real-use-cases.md), you will explore real-world use cases and integration patterns.
