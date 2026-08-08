using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc;
using EricksonLopez.Outbox.Persistence;

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
    }
}
