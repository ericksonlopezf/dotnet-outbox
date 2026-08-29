<!-- Copyright © Erickson Lopez. MIT License. -->

# Migration Guide

This guide describes breaking changes introduced across versions of `EricksonLopez.Outbox`
and how to adapt your code.

---

## Migrating from v1.0.0 to v2.0.0

`EricksonLopez.Outbox` v2.0.0 introduces architecture segregation, performance optimizations, and breaking contract changes to establish a cleaner domain/abstractions boundary.

### 1. `IIntegrationEvent` Interface Deletion
- **Change**: `IIntegrationEvent : IMessage` has been deleted from `EricksonLopez.Outbox.Contracts`.
- **Action**: Remove `: IIntegrationEvent` from your message classes/records. Decorate POCO message models with `[OutboxMessage(Topic = "...")]` or use the first-class domain event publishers in `EricksonLopez.Outbox.Events` / `EricksonLopez.Outbox.Inbox.Events`.

```diff
- public sealed record OrderCreatedEvent(Guid Id, decimal Amount) : IIntegrationEvent;
+ [OutboxMessage(Topic = "orders.created")]
+ public sealed record OrderCreatedEvent(Guid Id, decimal Amount);
```

### 2. `IOutbox.Publish` Moved to Extension Method
- **Change**: `Publish<TMessage>` has been removed from the `IOutbox` interface to keep the persistence SPI minimal. It is now provided as an extension method in `EricksonLopez.Outbox.OutboxPublishExtensions`.
- **Action**: Add `using EricksonLopez.Outbox;`. In test doubles/mocks, configure and assert `IOutbox.StoreAsync(...)` instead of `Publish`.

### 3. `MessageMetadata` Renamed to `OutboxMessageMetadata`
- **Change**: `MessageMetadata` has been renamed to `readonly struct OutboxMessageMetadata` and moved to `EricksonLopez.Outbox.Abstractions`.
- **Action**: Replace `MessageMetadata` with `OutboxMessageMetadata` and add `using EricksonLopez.Outbox.Abstractions;`.

```diff
- using EricksonLopez.Outbox;
- public ValueTask<DispatchResult> PublishRawAsync(OutboxMessage message, MessageMetadata metadata, DispatchContext context);
+ using EricksonLopez.Outbox.Abstractions;
+ public ValueTask<DispatchResult> PublishRawAsync(OutboxMessage message, OutboxMessageMetadata metadata, DispatchContext context);
```

### 4. Core Abstractions Assembly Segregation
- **Change**: All foundational contracts (`IOutbox`, `[OutboxMessage]`, `[IdempotentConsumer]`, `OutboxMessageStatus`, `IOutboxSerializer`, etc.) now reside in `EricksonLopez.Outbox.Abstractions.dll`.
- **Action**: Reference `EricksonLopez.Outbox.Abstractions` in domain and application projects instead of referencing the full `EricksonLopez.Outbox` implementation package.

### 5. MassTransit Native AOT Flag Update
- **Change**: `EricksonLopez.Outbox.MassTransit` has set `IsAotCompatible=false` due to MassTransit's upstream reflection architecture.
- **Action**: For Native AOT deployments, use raw broker transport packages (`EricksonLopez.Outbox.Brokers.RabbitMQ`, `Kafka`, `AzureServiceBus`, `AwsSqs`, `GooglePubSub`, `Nats`, `RedisStreams`) or `EricksonLopez.Outbox.Events`.

### 6. Consolidated PostgreSQL DDL
- **Change**: Separate numbered SQL scripts are now unified into `scripts/postgres/Outbox_DDL.sql`.
- **Action**: Update automated database migration runners (Flyway, DbUp, etc.) to target `scripts/postgres/Outbox_DDL.sql`.

---

## v1.0.0 — Initial Release

`EricksonLopez.Outbox` v1.0.0 is the initial public release of the library.
- Initial public release of core dispatcher, storage providers, broker publishers, and Roslyn analyzers.
- Zero-Reflection and Native AOT runtime guarantees across core modules.
