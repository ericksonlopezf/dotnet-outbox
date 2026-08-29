<!-- Copyright © Erickson Lopez. MIT License. -->

# Multi-Tenancy Architecture & Integration Guide

`EricksonLopez.Outbox` provides multi-tenancy primitives for multi-broker routing and multi-database architectures. These are **extension points** — contracts you implement and register via DI. The library does not auto-discover or register these implementations.

---

## 1. Core Abstractions

Multi-tenancy in `EricksonLopez.Outbox` is governed by two foundational interfaces:

- **`ITenantBrokerRouter`** — Dynamically resolves the target topic/queue destination based on `tenantId`.
- **`ITenantConnectionResolver`** — Resolves the database connection string for a given tenant (DB-per-tenant / schema-per-tenant patterns).

Every outbox message carries an optional tenant identifier encoded as the `x-tenant-id` header. The `OutboxMessageBuilder.WithTenantId()` method sets this header on a per-message basis.

---

## 2. Setting Tenant Context on Enqueue

### Fluent Message Builder (`WithTenantId`)

```csharp
using EricksonLopez.Outbox;

// WithTenantId() adds the reserved header "x-tenant-id" to the message.
// The dispatcher forwards this header to the broker publisher, where
// ITenantBrokerRouter can use it to resolve the destination topic/queue.
await outbox.Publish(new OrderCreatedEvent(orderId))
    .WithTenantId("tenant-enterprise-01")
    .WithTransaction(tx.ToOutboxContext())
    .StoreAsync(cancellationToken);

// Equivalent explicit form using WithHeader():
await outbox.Publish(new OrderCreatedEvent(orderId))
    .WithHeader("x-tenant-id", "tenant-enterprise-01")
    .WithTransaction(tx.ToOutboxContext())
    .StoreAsync(cancellationToken);
```

---

## 3. `ITenantBrokerRouter` — Tenant-Aware Broker Routing

Implement this interface to route messages from different tenants to different topics, exchanges, or queue destinations:

```csharp
using EricksonLopez.Outbox.MultiTenancy;

public interface ITenantBrokerRouter
{
    // Resolves the message destination for a given tenant.
    // tenantId         — the value from the x-tenant-id header (may be null for non-tenant messages)
    // baseDestination  — the configured default destination
    // messageType      — the message type alias (e.g., "order.created.v1")
    // Returns          — the final routing destination (topic, queue, exchange name)
    string ResolveDestination(string? tenantId, string baseDestination, string messageType);
}
```

### Implementation Example

```csharp
public sealed class TenantPrefixBrokerRouter : ITenantBrokerRouter
{
    // Routes: "acme" + "order.created.v1" → "acme.order.created.v1"
    public string ResolveDestination(string? tenantId, string baseDestination, string messageType)
        => tenantId is null ? baseDestination : $"{tenantId}.{messageType}";
}

// Registration:
services.AddSingleton<ITenantBrokerRouter, TenantPrefixBrokerRouter>();
```

### Compliance-Based Routing Example

```csharp
public sealed class ComplianceBrokerRouter : ITenantBrokerRouter
{
    public string ResolveDestination(string? tenantId, string baseDestination, string messageType)
        => tenantId switch
        {
            "tenant-eu-gdpr"  => $"kafka-europe.{messageType}",
            "tenant-us-hipaa" => $"azure-servicebus-us.{messageType}",
            _                 => baseDestination  // fall back to default destination
        };
}
```

---

## 4. `ITenantConnectionResolver` — Tenant-Aware Database Connection

For DB-per-tenant or schema-per-tenant architectures, implement this interface to resolve a tenant-specific connection string:

```csharp
using EricksonLopez.Outbox.MultiTenancy;

public interface ITenantConnectionResolver
{
    // Resolves the connection string for the specified tenant.
    ValueTask<string> ResolveConnectionStringAsync(
        string tenantId,
        CancellationToken cancellationToken = default);
}
```

### Implementation Example

```csharp
public sealed class CatalogTenantConnectionResolver : ITenantConnectionResolver
{
    private readonly IConfiguration _configuration;

    public CatalogTenantConnectionResolver(IConfiguration configuration)
        => _configuration = configuration;

    public ValueTask<string> ResolveConnectionStringAsync(string tenantId, CancellationToken ct)
    {
        // Look up tenant-specific connection string from configuration:
        // appsettings.json: { "Tenants": { "acme": { "ConnectionString": "..." } } }
        var cs = _configuration[$"Tenants:{tenantId}:ConnectionString"]
            ?? _configuration.GetConnectionString("DefaultOutbox")
            ?? throw new InvalidOperationException($"No connection string for tenant '{tenantId}'.");

        return ValueTask.FromResult(cs);
    }
}

// Registration:
services.AddScoped<ITenantConnectionResolver, CatalogTenantConnectionResolver>();
```

---

## 5. Security & Isolation Invariants

1. **Header propagation**: `WithTenantId()` encodes the tenant identity into the `x-tenant-id` message header. This header is persisted in the outbox and forwarded to the broker publisher, enabling downstream consumers to enforce tenant isolation.

2. **Partition pruning**: The `IOutboxRepository.GetMessageAsync(id, createdAtHint, ct)` overload provides a PostgreSQL partition pruning hint for range-partitioned tables, enabling efficient per-tenant message lookups without full table scans.

3. **No automatic wiring**: If neither `ITenantBrokerRouter` nor `ITenantConnectionResolver` is registered in DI, the library uses its standard routing: the default broker and the single configured connection string. Both interfaces are fully opt-in.
