// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using System.Threading.Tasks;

namespace Sample.OrderService.Endpoints;

/// <summary>
/// Level 11 — Administration and Monitoring
/// Demonstrates how to use IOutboxRepository and IDeadLetterRepository
/// to build administrative panels, health dashboards, and DLQ management.
/// </summary>
public static class Level11_AdministrationEndpoints
{
    public static void MapLevel11Administration(this IEndpointRouteBuilder app)
    {
        // ─── Endpoint 11a: Monitor Outbox Queue ────────────────────────────
        // IOutboxRepository provides GetPendingCountAsync() which is very fast
        // (usually does an approximate count or COUNT(*) on a covering index).
        // It's the same method used by the OutboxHealthCheck.
        app.MapGet("/api/level11/outbox/pending-count", async (
            [FromServices] IOutboxRepository outboxRepository,
            CancellationToken ct) =>
        {
            var pendingCount = await outboxRepository.GetPendingCountAsync(ct);

            return Results.Ok(new
            {
                description = "Current number of pending messages waiting to be dispatched.",
                pendingCount = pendingCount,
                alertLevel = pendingCount > 1000 ? "Warning - Queue is growing!" : "Healthy"
            });
        })
        .WithSummary("Level 11a - Monitor pending messages count (IOutboxRepository)")
        .WithTags("Level 11 — Administration");

        // ─── Endpoint 11b: View Dead Letter Queue (DLQ) ────────────────────
        // IDeadLetterRepository allows paginated querying of messages that failed fatally
        // or exhausted their retry policies.
        app.MapGet("/api/level11/dlq", async (
            [FromQuery] int limit,
            [FromQuery] DateTimeOffset? after,
            [FromServices] IDeadLetterRepository dlqRepository,
            CancellationToken ct) =>
        {
            // Default limit if not provided
            if (limit <= 0) limit = 50;

            // GetAsync returns a list of DeadLetterMessage sorted by DeadLetteredAt
            var dlqMessages = await dlqRepository.GetAsync(limit, after, ct);

            return Results.Ok(new
            {
                description = "Dead Letter Queue inspection.",
                returnedCount = dlqMessages.Count,
                messages = dlqMessages
            });
        })
        .WithSummary("Level 11b - View Dead Letter Queue (IDeadLetterRepository)")
        .WithTags("Level 11 — Administration");

        // ─── Endpoint 11c: Delete from DLQ ─────────────────────────────────
        // IDeadLetterRepository allows deleting specific messages from the DLQ
        // after they have been manually inspected or re-processed out-of-band.
        app.MapDelete("/api/level11/dlq/{id:guid}", async (
            Guid id,
            [FromServices] IDeadLetterRepository dlqRepository,
            CancellationToken ct) =>
        {
            await dlqRepository.DeleteAsync(id, ct);

            return Results.Ok(new
            {
                message = $"Message {id} removed from the Dead Letter Queue."
            });
        })
        .WithSummary("Level 11c - Delete message from Dead Letter Queue")
        .WithTags("Level 11 — Administration");

        // ─── Endpoint 11d: GetMessageAsync — single message lookup ──────────
        // IOutboxRepository.GetMessageAsync(id, ct) retrieves a single outbox message
        // by its ID regardless of state. This is a Default Interface Method (DIM):
        // it throws NotSupportedException unless the storage engine overrides it.
        //
        // The overload GetMessageAsync(id, createdAtHint, ct) adds a partition pruning
        // hint for range-partitioned table deployments (e.g., PostgreSQL PARTITION BY RANGE).
        app.MapGet("/api/level11/outbox/message/{id:guid}", async (
            Guid id,
            [FromQuery] DateTimeOffset? createdAt,
            [FromServices] IOutboxRepository outboxRepository,
            CancellationToken ct) =>
        {
            try
            {
                // If createdAt hint is provided, use the partition-pruning overload.
                // This is significantly faster in range-partitioned deployments because
                // the query planner can prune all partitions except the one containing this message.
                OutboxMessage? message = createdAt.HasValue
                    ? await outboxRepository.GetMessageAsync(id, createdAt.Value, ct)
                    : await outboxRepository.GetMessageAsync(id, ct);

                if (message is null)
                {
                    return Results.NotFound(new { error = $"Message {id} not found in the outbox." });
                }

                return Results.Ok(new
                {
                    description = "Single outbox message retrieved via IOutboxRepository.GetMessageAsync().",
                    id = message.Id,
                    messageType = message.MessageType,
                    status = message.Status.ToString(),
                    retryCount = message.RetryCount,
                    createdAt = message.CreatedAt,
                    deliverAt = message.DeliverAt,
                    processedAt = message.ProcessedAt,
                    error = message.Error,
                    partitionPruningHintUsed = createdAt.HasValue,
                });
            }
            catch (NotSupportedException ex)
            {
                return Results.Problem(
                    title: "GetMessageAsync not supported",
                    detail: ex.Message + " Note: This Default Interface Method requires an explicit " +
                        "override in the storage engine implementation (e.g., PostgreSqlOutboxRepository).",
                    statusCode: 501);
            }
        })
        .WithSummary("Level 11d - GetMessageAsync(id) / GetMessageAsync(id, createdAtHint): single message lookup")
        .WithTags("Level 11 — Administration");

        // ─── Endpoint 11e: PurgeDispatchedMessagesAsync — manual retention ──
        // IOutboxRepository.PurgeDispatchedMessagesAsync(cutoff, batchSize, ct)
        // deletes dispatched messages older than 'cutoff' in batches.
        // Only relevant when OutboxRuntimeOptions.DeleteOnDispatch = false (soft-delete mode).
        // In the default configuration (DeleteOnDispatch = true), messages are deleted immediately
        // on dispatch and this method has no effect.
        //
        // The OutboxCleanupService background worker calls this automatically when enabled.
        // Use this endpoint for manual, on-demand retention control or administrative cleanup.
        app.MapDelete("/api/level11/outbox/purge-dispatched", async (
            [FromQuery] int? olderThanDays,
            [FromServices] IOutboxRepository outboxRepository,
            CancellationToken ct) =>
        {
            // Default: purge messages dispatched more than 7 days ago
            var days = olderThanDays ?? 7;
            var cutoff = DateTimeOffset.UtcNow.AddDays(-days);

            // PurgeDispatchedMessagesAsync(cutoff, batchSize, ct):
            //   cutoff   — delete messages with ProcessedAt < cutoff
            //   batchSize — max rows per DELETE to avoid lock escalation (default: 1000)
            //   returns  — count of rows deleted
            //
            // NOTE: This has NO EFFECT when DeleteOnDispatch = true (default).
            // Enable soft-delete via: options.ConfigureRuntimeOptions(r => r.DeleteOnDispatch = false)
            var purgedCount = await outboxRepository.PurgeDispatchedMessagesAsync(
                cutoff: cutoff,
                batchSize: 1000,
                cancellationToken: ct);

            return Results.Ok(new
            {
                description = "Manual purge of dispatched messages via IOutboxRepository.PurgeDispatchedMessagesAsync().",
                cutoff = cutoff,
                olderThanDays = days,
                purgedCount,
                note = purgedCount == 0
                    ? "0 rows purged. Verify that DeleteOnDispatch=false is configured. " +
                      "In the default configuration (DeleteOnDispatch=true) messages are deleted immediately on dispatch."
                    : $"{purgedCount} dispatched messages purged successfully.",
                automaticAlternative = "Use services.AddOutboxCleanupService(options => { options.Enabled = true; " +
                    "options.RetentionPeriod = TimeSpan.FromDays(7); options.CleanupInterval = TimeSpan.FromHours(1); }) " +
                    "to run this automatically in the background."
            });
        })
        .WithSummary("Level 11e - PurgeDispatchedMessagesAsync(): manual retention control (soft-delete mode)")
        .WithTags("Level 11 — Administration");
    }
}


