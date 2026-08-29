// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Npgsql;
using Sample.OrderService.Domain.Aggregates.OrderAggregate;
using System.Threading.Tasks;

#pragma warning disable CA1861 // Prefer static readonly fields over constant array arguments
namespace Sample.OrderService.Endpoints;

/// <summary>
/// Level 5 — Processing
/// Demonstrates batch insertion (Batch Processing) with all available overloads.
/// </summary>
public static class Level5_ProcessingEndpoints
{
    public static void MapLevel5Processing(this IEndpointRouteBuilder app)
    {
        // ─── Endpoint 5a: Batch via IEnumerable<T> (extension) ───────────────
        // OutboxExtensions.StoreAsync(IEnumerable<T>, ...) internally converts
        // the sequence to ReadOnlyMemory<T> using an array. Convenient for
        // small collections where the materialization overhead is acceptable.
        app.MapPost("/api/level5/batch", async (
            [FromServices] IOutbox outbox,
            [FromServices] NpgsqlDataSource dataSource,
            CancellationToken ct) =>
        {
            await using var conn = await dataSource.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            var events = Enumerable.Range(1, 10)
                .Select(i => new OrderCreatedEvent(Guid.NewGuid(), $"BATCH-{i}", 100m * i, DateTimeOffset.UtcNow));

            // StoreAsync(IEnumerable<T>, ...) — convenient extension.
            // Internally: materializes to array → ReadOnlyMemory<T> → InsertBatchAsync SQL.
            await outbox.StoreAsync(events, tx.ToOutboxContext(), ct);

            await tx.CommitAsync(ct);

            return Results.Ok(new { message = "Level 5a: 10 messages enqueued via IEnumerable." });
        })
        .WithSummary("Level 5a - IOutbox.StoreAsync(IEnumerable<T>) — batch via extension")
        .WithTags("Level 5 — Processing");

        // ─── Endpoint 5b: Batch via ReadOnlyMemory<T> (native overload) ──────
        // IOutbox.StoreAsync(ReadOnlyMemory<T>, ...) is the most efficient overload.
        // It avoids the intermediate ToArray() from IEnumerable. Ideal for:
        //   - Pre-rented buffers from ArrayPool.
        //   - Arrays with a size known at compile-time.
        //   - Hot paths with high throughput.
        app.MapPost("/api/level5/batch-memory", async (
            [FromServices] IOutbox outbox,
            [FromServices] NpgsqlDataSource dataSource,
            CancellationToken ct) =>
        {
            await using var conn = await dataSource.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            // Pre-allocate the array of events with exact size.
            const int batchCount = 5;
            var events = new OrderCreatedEvent[batchCount];
            for (int i = 0; i < batchCount; i++)
            {
                events[i] = new OrderCreatedEvent(
                    Guid.NewGuid(), $"MEM-BATCH-{i + 1}", 200m * (i + 1), DateTimeOffset.UtcNow);
            }

            // ReadOnlyMemory<T> overload — zero intermediate allocations.
            // Passes directly to the InsertBatchAsync of the IOutboxRepository implementation.
            var memory = new ReadOnlyMemory<OrderCreatedEvent>(events);
            await outbox.StoreAsync(memory, tx.ToOutboxContext(), ct);

            await tx.CommitAsync(ct);

            return Results.Ok(new
            {
                message = $"Level 5b: {batchCount} messages enqueued via ReadOnlyMemory<T> (zero-allocation path)."
            });
        })
        .WithSummary("Level 5b - IOutbox.StoreAsync(ReadOnlyMemory<T>) — high-performance batch")
        .WithTags("Level 5 — Processing");

        // ─── Endpoint 5c: StoreAsync with explicit metadata ──────────────────
        // IOutbox.StoreAsync<TMessage>(message, transaction, metadata, deliverAt, ct)
        // — the maximum control overload: payload + metadata + scheduling in a single call.
        // Avoids the overhead of OutboxMessageBuilder when metadata is already built.
        app.MapPost("/api/level5/store-with-metadata", async (
            [FromServices] IOutbox outbox,
            [FromServices] NpgsqlDataSource dataSource,
            CancellationToken ct) =>
        {
            await using var conn = await dataSource.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            var @event = new BatchTestEvent(Index: 42, Data: "explicit-metadata-demo");

            // We build OutboxMessageMetadata directly — without builder.
            // Useful in infrastructure code where metadata already exists.
            var metadata = new OutboxMessageMetadata(
                correlationId: Guid.NewGuid().ToString("N"),
                causationId: Guid.NewGuid().ToString("N"),
                messageType: null,  // null → the serializer uses the alias from the [OutboxMessage] attribute
                entries: new ReadOnlyMemory<MetadataEntry>(new[]
                {
                    new MetadataEntry("X-Batch-Index", "42"),
                    new MetadataEntry("X-Source-System", "showcase"),
                }));

            // Full overload with explicit metadata and scheduling.
            await outbox.StoreAsync(@event, tx.ToOutboxContext(), metadata, deliverAt: null, ct);

            await tx.CommitAsync(ct);

            return Results.Ok(new { message = "Level 5c: StoreAsync with explicit OutboxMessageMetadata." });
        })
        .WithSummary("Level 5c - IOutbox.StoreAsync(msg, tx, metadata, deliverAt) — full control overload")
        .WithTags("Level 5 — Processing");

        // ─── Endpoint 5d: Scheduled message delivery ──────────────────────────
        // OutboxMessageBuilder exposes two scheduling methods:
        //   - WithDelay(TimeSpan delay)       → deliverAt = UtcNow + delay
        //   - WithDeliverAt(DateTimeOffset)   → explicit absolute timestamp
        //
        // The message stays in Pending state (invisible to the dispatcher)
        // until the current UTC time >= deliverAt.
        //
        // IMPORTANT: deliverAt must not exceed OutboxRuntimeOptions.MaxMessageAge.
        // If it does, StoreAsync throws ArgumentOutOfRangeException.
        // Increase MaxMessageAge if you need scheduling horizons > 30 days (the default).
        app.MapPost("/api/level5/scheduled-delivery", async (
            [FromServices] IOutbox outbox,
            [FromServices] NpgsqlDataSource dataSource,
            CancellationToken ct) =>
        {
            await using var conn = await dataSource.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            var @event = new OrderCreatedEvent(Guid.NewGuid(), "CUST-SCHED", 500m, DateTimeOffset.UtcNow);

            // WithDelay: stores the message now, but delays dispatch by 30 seconds.
            // Use case: retry-after-a-period, delayed notifications, scheduled reminders.
            await outbox.Publish(@event)
                .WithTransaction(tx.ToOutboxContext())
                .WithDelay(TimeSpan.FromSeconds(30))
                .StoreAsync(ct);

            // WithDeliverAt: explicit absolute UTC timestamp.
            // Use case: scheduled campaigns, time-zone-aware scheduling, future events.
            var futureEvent = new OrderConfirmedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow);
            var deliverAt = DateTimeOffset.UtcNow.AddMinutes(5);

            await outbox.Publish(futureEvent)
                .WithTransaction(tx.ToOutboxContext())
                .WithDeliverAt(deliverAt)
                .StoreAsync(ct);

            await tx.CommitAsync(ct);

            return Results.Ok(new
            {
                message = "Level 5d: Two scheduled messages stored.",
                withDelay = new
                {
                    description = "WithDelay(30s) — dispatches 30 seconds from now.",
                    apiSignature = "OutboxMessageBuilder<TMessage>.WithDelay(TimeSpan delay)",
                    effectiveDeliverAt = DateTimeOffset.UtcNow.AddSeconds(30),
                },
                withDeliverAt = new
                {
                    description = "WithDeliverAt — explicit absolute timestamp.",
                    apiSignature = "OutboxMessageBuilder<TMessage>.WithDeliverAt(DateTimeOffset deliverAt)",
                    effectiveDeliverAt = deliverAt,
                },
                warning = "If deliverAt >= (UtcNow + OutboxRuntimeOptions.MaxMessageAge), StoreAsync throws ArgumentOutOfRangeException. " +
                    "Increase MaxMessageAge to support longer scheduling horizons."
            });
        })
        .WithSummary("Level 5d - WithDelay() and WithDeliverAt(): scheduled message delivery")
        .WithTags("Level 5 — Processing");

        // ─── Endpoint 5e: EnqueueAsync() — semantic alias for StoreAsync() ────
        // OutboxPublishExtensions.EnqueueAsync() is a semantic alias for StoreAsync().
        // It provides the same functionality with a different name that aligns better
        // with queue-oriented mental models (MediatR, NServiceBus, MassTransit terminology).
        //
        // Available overloads:
        //   1. EnqueueAsync<T>(message, transaction, ct)                   → single message
        //   2. EnqueueAsync<T>(ReadOnlyMemory<T>, transaction, ct)          → batch (zero-alloc)
        //   3. EnqueueAsync<T>(IEnumerable<T>, transaction, ct)             → batch (LINQ-friendly)
        //   4. EnqueueAsync<T>(msg, tx, OutboxMessageMetadata, deliverAt, ct) → full control
        app.MapPost("/api/level5/enqueue-async", async (
            [FromServices] IOutbox outbox,
            [FromServices] NpgsqlDataSource dataSource,
            CancellationToken ct) =>
        {
            await using var conn = await dataSource.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            var context = tx.ToOutboxContext();

            // Overload 1: single message — direct semantic alias for StoreAsync<T>(msg, tx, ct)
            var singleEvent = new OrderCreatedEvent(Guid.NewGuid(), "CUST-ENQ", 100m, DateTimeOffset.UtcNow);
            await outbox.EnqueueAsync(singleEvent, context, ct);

            // Overload 3: IEnumerable batch — alias for StoreAsync(IEnumerable<T>, ...)
            var batchEvents = Enumerable.Range(1, 3)
                .Select(i => new OrderCreatedEvent(Guid.NewGuid(), $"CUST-ENQ-BATCH-{i}", 50m * i, DateTimeOffset.UtcNow));
            await outbox.EnqueueAsync(batchEvents, context, ct);

            await tx.CommitAsync(ct);

            return Results.Ok(new
            {
                message = "Level 5e: EnqueueAsync() overloads demonstrated.",
                note = "EnqueueAsync() is a semantic alias for StoreAsync(). Use whichever name fits your team's domain language.",
                overloads = new[]
                {
                    "EnqueueAsync<T>(TMessage msg, IOutboxTransactionContext tx, CancellationToken ct) — single message",
                    "EnqueueAsync<T>(ReadOnlyMemory<T> msgs, IOutboxTransactionContext tx, CancellationToken ct) — batch (zero-alloc)",
                    "EnqueueAsync<T>(IEnumerable<T> msgs, IOutboxTransactionContext tx, CancellationToken ct) — batch",
                    "EnqueueAsync<T>(TMessage msg, IOutboxTransactionContext tx, OutboxMessageMetadata metadata, DateTimeOffset? deliverAt, CancellationToken ct) — full control",
                }
            });
        })
        .WithSummary("Level 5e - EnqueueAsync(): semantic alias for StoreAsync() with all overloads")
        .WithTags("Level 5 — Processing");
    }
}



