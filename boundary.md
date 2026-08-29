<!-- Copyright © Erickson Lopez. MIT License. -->

# Architectural Boundary Specification: EricksonLopez.Outbox.Abstractions

## 1. Purpose
`EricksonLopez.Outbox.Abstractions` defines the client contracts and storage abstraction SPI for the Transactional Outbox and Inbox patterns in .NET applications, guaranteeing at-least-once message delivery and exactly-once processing semantics without distributed transactions.

## 2. Owns
- `IOutbox` client contract.
- `IOutboxMessage`, `OutboxMessageMetadata`, `OutboxMessageStatus`.
- `IOutboxStore` storage SPI contract.
- `IInboxStore`, `IInboxMessage` (in `EricksonLopez.Inbox.Abstractions`).

## 3. Does Not Own
- Background processing engine or worker loop (`EricksonLopez.Outbox`).
- Concrete database persistence (`EricksonLopez.Outbox.Storage.PostgreSql`, `SqlServer`, etc.).
- Concrete broker publishing (`EricksonLopez.Outbox.Brokers.RabbitMQ`, `Kafka`, etc.).
- In-process mediator pipeline triggers (`EricksonLopez.Outbox.Mediator`).

## 4. Allowed Dependencies
- **.NET BCL only**.
- **Zero** external dependencies.

## 5. Forbidden Dependencies
- Database driver SDKs (`Npgsql`, `Microsoft.Data.SqlClient`, `MySqlConnector`, `Oracle.ManagedDataAccess`).
- Broker SDKs (`RabbitMQ.Client`, `Confluent.Kafka`, `AWSSDK.SQS`, `Azure.Messaging.ServiceBus`).
- `Microsoft.AspNetCore.*`.

## 6. Who Can Depend On It
- `EricksonLopez.Outbox` (core engine).
- `EricksonLopez.Outbox.Storage.*` (storage provider adapters).
- Application and Domain services publishing outbox messages.

## 7. Public API Rules
- Contracts must not expose database connection primitives (`IDbConnection`, `DbTransaction`).

## 8. AOT Expectations
- `IsAotCompatible=true`.

## 9. Trimming Expectations
- `IsTrimmable=true`.

## 10. Provider Isolation
- 100% database- and broker-agnostic.

## 11. Testing Isolation
- InMemory outbox stores and test fakes live in test harnesses.
