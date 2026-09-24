// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

#pragma warning disable CA1861 // Prefer static readonly fields over constant array arguments
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

        // ─── Endpoint 11f: ReclaimStaleMessagesAsync — crash recovery ──────────
        // IOutboxRepository.ReclaimStaleMessagesAsync(staleTimeout, ct) is the
        // crash recovery mechanism of the outbox dispatcher.
        //
        // When a dispatcher instance claims messages (InFlight state = 1) and then
        // crashes before marking them as dispatched, those messages become stuck.
        // ReclaimStaleMessagesAsync resets them back to Pending (0) so they can
        // be re-fetched and dispatched by the next available dispatcher instance.
        //
        // The OutboxDispatcherBackgroundService calls this automatically via the
        // reclaim cycle (configured by OutboxDispatcherOptions.ReclaimInterval and
        // OutboxDispatcherOptions.ReclaimTimeout). This endpoint demonstrates the
        // low-level call for administrative or testing purposes.
        app.MapPost("/api/level11/outbox/reclaim-stale", async (
            [FromServices] IOutboxRepository outboxRepository,
            CancellationToken ct) =>
        {
            // ReclaimStaleMessagesAsync(staleTimeout, ct):
            //   staleTimeout — a message in InFlight state older than (UtcNow - staleTimeout)
            //                  is considered abandoned and reset to Pending.
            //   Returns     — the number of messages reclaimed.
            //
            // Typical value: 5 minutes (matches OutboxDispatcherOptions.ReclaimTimeout default).
            // Lower values risk false-positives: reclaiming messages that are still being processed.
            // Higher values increase the time a crashed dispatcher's messages stay stuck.
            var reclaimedCount = await outboxRepository.ReclaimStaleMessagesAsync(
                staleTimeout: TimeSpan.FromMinutes(5),
                cancellationToken: ct);

            return Results.Ok(new
            {
                description = "IOutboxRepository.ReclaimStaleMessagesAsync() — crash recovery for stuck InFlight messages.",
                reclaimedCount,
                staleTimeout = "5 minutes",
                howItWorks = new[]
                {
                    "1. The dispatcher atomically transitions messages from Pending(0) to InFlight(1) on FetchPendingAsync().",
                    "2. If the dispatcher crashes after step 1 but before MarkAsDispatchedAsync(), messages remain InFlight indefinitely.",
                    "3. ReclaimStaleMessagesAsync() resets any InFlight message whose updated_at < (UtcNow - staleTimeout) back to Pending(0).",
                    "4. The next FetchPendingAsync() cycle will re-claim and re-dispatch those messages.",
                },
                automaticBehavior = "The OutboxDispatcherBackgroundService calls ReclaimStaleMessagesAsync() " +
                    "automatically every OutboxDispatcherOptions.ReclaimInterval (default: 1 minute). " +
                    "Manual calls are only needed for administrative tooling or testing.",
                idempotent = true,
                note = "OutboxDispatcherOptions.ReclaimBatchLimit (OutboxRuntimeOptions) caps how many " +
                       "messages are reclaimed per cycle to prevent large lock waves on the database."
            });
        })
        .WithSummary("Level 11f - ReclaimStaleMessagesAsync(): crash recovery for stuck InFlight messages")
        .WithTags("Level 11 — Administration");

        // ─── Endpoint 11g: IDeadLetterRepository — complete contract reference ─
        // IDeadLetterRepository has 4 methods. Level 11b covered GetAsync and
        // Level 11c covered DeleteAsync. This endpoint documents the remaining two:
        //   PurgeAsync(olderThan, ct) — bulk delete of aged DLQ entries
        //   IsFirstPartyImplementation — DIM property for startup validation
        //
        // Additionally documents DeadLetterMessage.FromOutboxMessage() — the factory
        // that the dispatcher uses to construct a DeadLetterMessage from an OutboxMessage.
        app.MapDelete("/api/level11/dlq/purge", async (
            [FromQuery] int? olderThanDays,
            [FromServices] IDeadLetterRepository dlqRepository,
            CancellationToken ct) =>
        {
            var days = olderThanDays ?? 30;
            var olderThan = DateTimeOffset.UtcNow.AddDays(-days);

            // PurgeAsync(olderThan, ct):
            //   Bulk-deletes all dead-lettered messages whose DeadLetteredAt < olderThan.
            //   Unlike IOutboxRepository.PurgeDispatchedMessagesAsync, this has no batchSize
            //   parameter — DLQ tables are typically small so a single DELETE is acceptable.
            await dlqRepository.PurgeAsync(olderThan, ct);

            return Results.Ok(new
            {
                description = "IDeadLetterRepository.PurgeAsync() — bulk delete of aged DLQ entries.",
                olderThan,
                olderThanDays = days,
                notes = new[]
                {
                    "PurgeAsync deletes ALL dead-lettered messages with DeadLetteredAt older than the cutoff — not just a batch.",
                    "DLQ entries are typically low-volume (only messages that exhausted retries), so a full delete per run is safe.",
                    "For high-volume DLQ scenarios, implement a custom IDeadLetterRepository with batched PurgeAsync.",
                },
                allDeadLetterRepositoryMethods = new[]
                {
                    "InsertAsync(DeadLetterMessage, IOutboxTransactionContext?, ct) — persists a dead-lettered message. Called by the dispatcher.",
                    "GetAsync(limit, after?, ct) → IReadOnlyList<DeadLetterMessage> — paginated retrieval for inspection or replay UI.",
                    "DeleteAsync(Guid id, ct) — removes a single entry (after manual inspection or re-queue).",
                    "PurgeAsync(DateTimeOffset olderThan, ct) — bulk delete for retention policies.",
                    "bool IsFirstPartyImplementation { get; } — DIM property. Returns false by default. First-party impls override to true.",
                },
                isFirstPartyImplementation = new
                {
                    property = "bool IsFirstPartyImplementation => false",
                    purpose = "Used by OutboxStartupValidator to emit an advisory warning when a third-party IDeadLetterRepository is detected.",
                    warning = "Third-party implementations must correctly handle 'transaction = null' (auto-commit mode). " +
                              "The dispatcher calls InsertAsync with null transaction when dead-lettering outside a user transaction.",
                    firstPartyNote = "All storage engine implementations (PostgreSql, SqlServer, MySql, etc.) override this to return true. " +
                                     "If you implement IDeadLetterRepository yourself, leave the default (false) to get the startup advisory."
                },
                deadLetterMessageFactory = new
                {
                    method = "DeadLetterMessage.FromOutboxMessage(OutboxMessage original, int retryCount, string reason, string? lastError)",
                    purpose = "Factory method used by the dispatcher to construct a DeadLetterMessage from a failed OutboxMessage.",
                    fields = new[]
                    {
                        "Id — new Guid (or Guid.CreateVersion7() on .NET 9+)",
                        "OriginalMessageId — preserves the original OutboxMessage.Id for correlation",
                        "MessageType, Payload, CorrelationId, CausationId, Headers, CreatedAt — copied from original",
                        "DeadLetteredAt — DateTimeOffset.UtcNow at the time of dead-lettering",
                        "RetryCount — total attempts before giving up",
                        "Reason — brief reason string (e.g., 'MaxRetryCount exceeded')",
                        "LastError — full exception message/stack trace from the last failed attempt",
                    }
                }
            });
        })
        .WithSummary("Level 11g - IDeadLetterRepository: PurgeAsync, IsFirstPartyImplementation, DeadLetterMessage.FromOutboxMessage")
        .WithTags("Level 11 — Administration");

        // ─── Endpoint 11h: OutboxMessage — optional fields reference ────────────
        // OutboxMessage has two optional fields that are not part of the primary
        // store/dispatch hot path but are critical for multi-tenancy and extensibility:
        //   TenantId — set via WithTenantId() on the builder (Level 2b)
        //   Extensions — dictionary for custom routing metadata (future v2.0 typed values)
        //
        // This endpoint provides the reference for these fields and their interaction
        // with the dispatch pipeline.
        app.MapGet("/api/level11/outbox/message-optional-fields", () =>
        {
            return Results.Ok(new
            {
                description = "OutboxMessage optional fields: TenantId and Extensions.",
                fields = new object[]
                {
                    new
                    {
                        field = "string? TenantId { get; init; }",
                        setVia = "OutboxMessageBuilder.WithTenantId(string tenantId) — adds 'x-tenant-id' header AND sets TenantId.",
                        use = "Multi-tenant deployments: the broker publisher reads TenantId to route to a tenant-specific topic/queue " +
                              "via ITenantBrokerRouter.ResolveDestination().",
                        storedIn = "Stored as a dedicated column in the outbox table. Indexed for fast per-tenant queries.",
                        example = @"
await outbox.Publish(new OrderCreatedEvent(...))
    .WithTenantId(""acme"")          // → sets x-tenant-id header + TenantId column
    .WithTransaction(tx.ToOutboxContext())
    .StoreAsync(ct);"
                    },
                    new
                    {
                        field = "IReadOnlyDictionary<string, string>? Extensions { get; init; }",
                        setVia = "Currently not exposed via OutboxMessageBuilder. Set via the raw OutboxMessage constructor.",
                        use = "Reserved for future v2.0 typed routing metadata: Kafka partition offsets, CDC/WAL metadata, sequence numbers.",
                        roadmapNote = "ROADMAP-v2: Extensions will be upgraded from IReadOnlyDictionary<string, string> " +
                                      "to IReadOnlyDictionary<string, object?> or a typed ExtensionMetadata to support non-string values. " +
                                      "This is a binary-breaking change deferred from v1.0.",
                        currentStatus = "String-only in v1.0. Sufficient for string headers but precludes typed Kafka partition offsets."
                    },
                },
                multiTenantPatternReference = "See Level 2b (/api/level2/fluent-tenant) for the full multi-tenancy publishing pattern. " +
                                              "See Level 8i (/api/level8/multi-tenancy) for ITenantBrokerRouter and ITenantConnectionResolver implementations."
            });
        })
        .WithSummary("Level 11h - OutboxMessage.TenantId and Extensions: optional fields reference")
        .WithTags("Level 11 — Administration");
    }
}


