using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using EricksonLopez.Outbox.Idempotency;
using EricksonLopez.Outbox.Contracts;
using EricksonLopez.Outbox.Persistence;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Sample.OrderService.Endpoints;

/// <summary>
/// Level 10 — Enterprise Architecture: Inbox Pattern and Idempotency
/// Demonstrates IInboxIdempotencyChecker (ShouldProcessAsync and ShouldSkipAsync),
/// IdempotentConsumerAttribute, InboxConsumerAttribute, and OutboxInboxOptions.
/// </summary>
public static class Level10_InboxIdempotencyEndpoints
{
    public static void MapLevel10InboxIdempotency(this IEndpointRouteBuilder app)
    {
        // ─── Endpoint 10a: ShouldProcessAsync — atomic insertion ───────────
        // The Inbox pattern guarantees exactly-once processing.
        // ShouldProcessAsync attempts to insert an idempotency record atomically.
        // If the record already exists (duplicate message), it returns false.
        // If it doesn't exist, it inserts and returns true — the message should be processed.
        app.MapPost("/api/level10/inbox-check", async (
            [FromBody] EventPayload payload,
            [FromServices] IInboxIdempotencyChecker idempotencyChecker,
            [FromServices] NpgsqlDataSource dataSource,
            CancellationToken ct) =>
        {
            await using var conn = await dataSource.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            var dbTx = tx.ToOutboxContext();

            // ShouldProcessAsync(messageId, consumerId, transaction, ct):
            //   - messageId: unique ID of the message received from the broker.
            //   - consumerId: ID of the consumer (for multi-consumer of the same message).
            //   - transaction: the same TX of the business operation — full atomicity.
            //
            // Internally uses INSERT ... ON CONFLICT DO NOTHING (PostgreSQL)
            // or INSERT WHERE NOT EXISTS (SQL Server) to guarantee no race conditions.
            var shouldProcess = await idempotencyChecker.ShouldProcessAsync(
                messageId: payload.EventId.ToString(),
                consumerId: "Level10Consumer-v1",  // Versioning the consumerId allows resetting idempotency
                transaction: dbTx,
                cancellationToken: ct);

            if (!shouldProcess)
            {
                await tx.RollbackAsync(ct);
                return Results.Ok(new
                {
                    processed = false,
                    message = "Level 10a: Event IGNORED — already processed (idempotency).",
                    eventId = payload.EventId
                });
            }

            // Here goes the business logic of the consumer...
            // Everything in the SAME transaction: business + idempotency = atomicity.
            await tx.CommitAsync(ct);

            return Results.Ok(new
            {
                processed = true,
                message = "Level 10a: Event PROCESSED and recorded in Inbox atomically.",
                eventId = payload.EventId
            });
        })
        .WithSummary("Level 10a - IInboxIdempotencyChecker.ShouldProcessAsync(): atomic insertion")
        .WithTags("Level 10 — Inbox Pattern");

        // ─── Endpoint 10b: ShouldSkipAsync — idempotency query ────────
        // ShouldSkipAsync is the complement of ShouldProcessAsync.
        // It only QUERIES if the message was already processed, without attempting to insert.
        // Useful for compensation logic or pre-processing verification.
        app.MapPost("/api/level10/inbox-skip-check", async (
            [FromBody] EventPayload payload,
            [FromServices] IInboxIdempotencyChecker idempotencyChecker,
            [FromServices] NpgsqlDataSource dataSource,
            CancellationToken ct) =>
        {
            await using var conn = await dataSource.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            var dbTx = tx.ToOutboxContext();

            // ShouldSkipAsync(messageId, transaction, ct):
            //   - Queries if an idempotency record already exists for this messageId.
            //   - DOES NOT insert — only reads.
            //   - Returns true if the message was ALREADY processed (you should skip it).
            //   - Returns false if it is new (you can process it).
            var shouldSkip = await idempotencyChecker.ShouldSkipAsync(
                messageId: payload.EventId,
                transaction: dbTx,
                cancellationToken: ct);

            await tx.RollbackAsync(ct); // Only query, no commit needed

            return Results.Ok(new
            {
                shouldSkip,
                message = shouldSkip
                    ? "Level 10b: The message was already processed — ShouldSkipAsync returned true."
                    : "Level 10b: The message has not been processed — ShouldSkipAsync returned false.",
                difference = new
                {
                    ShouldProcessAsync = "Inserts atomically. Returns true if NEW (process). Returns false if DUPLICATE (ignore).",
                    ShouldSkipAsync = "Only queries. Returns true if ALREADY PROCESSED (skip). Returns false if NEW."
                }
            });
        })
        .WithSummary("Level 10b - IInboxIdempotencyChecker.ShouldSkipAsync(): idempotency query")
        .WithTags("Level 10 — Inbox Pattern");

        // ─── Endpoint 10c: OutboxInboxOptions and attributes ────────────────────
        // Documents the Inbox options and the available attributes.
        app.MapGet("/api/level10/inbox-options", () =>
        {
            return Results.Ok(new
            {
                description = "OutboxInboxOptions and attributes of the Inbox Pattern.",
                options = new[]
                {
                    new { property = "RetentionPeriod", defaultValue = "7 days", description = "How long idempotency records are retained. After this time, cleanup deletes them." },
                    new { property = "DuplicateDetectionWindow", defaultValue = "24 hours", description = "Time window during which duplicate messages are detected." },
                    new { property = "CleanupInterval", defaultValue = "1 hour", description = "Frequency of the background job that cleans up expired idempotency records (InboxCleanupService)." },
                },
                attributes = new[]
                {
                    new
                    {
                        attribute = "[IdempotentConsumer]",
                        target = "class",
                        purpose = "Marks a consumer (e.g. MediatR IRequestHandler) as explicitly idempotent.",
                        effect = "Suppresses the OUTBOX003 warning from the analyzer, which warns when a consumer doesn't implement idempotency.",
                        example = @"
[IdempotentConsumer]
public sealed class CreateOrderHandler : IRequestHandler<CreateOrderCommand, Guid>
{
    // This handler already implements idempotency manually.
    // [IdempotentConsumer] documents this decision and suppresses OUTBOX003.
}"
                    },
                    new
                    {
                        attribute = "[InboxConsumer(eventAlias)]",
                        target = "method",
                        purpose = "Marks a method as an Inbox consumer for a specific event type.",
                        effect = "The Source Generator wraps the method with idempotency verification and automatic transaction management.",
                        example = @"
public class OrderEventConsumer
{
    [InboxConsumer(""order.created.v1"")]
    public async Task HandleOrderCreatedAsync(OrderCreatedEvent @event, ...)
    {
        // The Source Generator will have executed ShouldProcessAsync() before calling this method.
        // If it is a duplicate, the method is not invoked.
    }
}"
                    },
                },
                registration = @"
// AddOutboxInbox() registers the Inbox services:
services.AddOutboxInbox(options =>
{
    options.RetentionPeriod = TimeSpan.FromDays(7);          // Records are retained for 7 days
    options.DuplicateDetectionWindow = TimeSpan.FromHours(24); // Deduplication window
    options.CleanupInterval = TimeSpan.FromHours(1);          // Cleanup every hour
});

// Registered services:
// - IInboxIdempotencyChecker (Scoped) → InboxIdempotencyChecker
// - IHostedService → InboxCleanupService (background cleanup job)"
            });
        })
        .WithSummary("Level 10c - OutboxInboxOptions + [IdempotentConsumer] + [InboxConsumer]")
        .WithTags("Level 10 — Inbox Pattern");
    }

    public record EventPayload(Guid EventId);
}
