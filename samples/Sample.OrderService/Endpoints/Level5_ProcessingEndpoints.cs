using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Sample.OrderService.Domain.Aggregates.OrderAggregate;
using System.Linq;

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

            // We build MessageMetadata directly — without builder.
            // Useful in infrastructure code where metadata already exists.
            var metadata = new MessageMetadata(
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

            return Results.Ok(new { message = "Level 5c: StoreAsync with explicit MessageMetadata." });
        })
        .WithSummary("Level 5c - IOutbox.StoreAsync(msg, tx, metadata, deliverAt) — full control overload")
        .WithTags("Level 5 — Processing");
    }
}
