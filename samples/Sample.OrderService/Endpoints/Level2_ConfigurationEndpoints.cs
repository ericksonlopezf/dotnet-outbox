// Copyright © Erickson Lopez. MIT License.
#pragma warning disable CA1861
using System;
using System.Collections.Generic;
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

namespace Sample.OrderService.Endpoints;

/// <summary>
/// Level 2 — Configuration and Fluent API
/// Demonstrates the OutboxMessageBuilder for detailed message configuration.
/// </summary>
public static class Level2_ConfigurationEndpoints
{
    public static void MapLevel2Configuration(this IEndpointRouteBuilder app)
    {
        // ─── Endpoint 2a: Full OutboxMessageBuilder API ───────────────────────
        // IOutbox.Publish(message) returns OutboxMessageBuilder<TMessage>.
        // The builder supports all enrichment options before StoreAsync().
        app.MapPost("/api/level2/fluent", async (
            [FromServices] IOutbox outbox,
            [FromServices] NpgsqlDataSource dataSource,
            CancellationToken ct) =>
        {
            await using var conn = await dataSource.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            var @event = new OrderCreatedEvent(Guid.NewGuid(), "CUST-1", 100m, DateTimeOffset.UtcNow);
            
            // Full configuration with Message Builder (Fluent API).
            // Builder methods:
            //   WithTransaction(IOutboxTransactionContext) — REQUIRED: sets the active transaction
            //   WithHeader(key, value)                     — adds a custom message header
            //   WithCorrelationId(string)                  — distributed tracing correlation ID
            //   WithCausationId(string)                    — identifies the causing operation
            //   WithTenantId(string)                       — multi-tenancy routing (adds x-tenant-id header)
            //   WithDelay(TimeSpan)                        — schedule dispatch after a delay
            //   WithDeliverAt(DateTimeOffset)              — schedule dispatch at an absolute UTC time
            //   StoreAsync(CancellationToken)              — persists and auto-disposes the builder
            await outbox.Publish(@event)
                .WithTransaction(tx.ToOutboxContext())
                .WithCorrelationId("corr-id-123")
                .WithCausationId("cause-id-123")
                .WithHeader("X-Correlation-Id", Guid.NewGuid().ToString())
                .WithHeader("X-Source-System", "showcase")
                .StoreAsync(ct);
                
            await tx.CommitAsync(ct);

            return Results.Ok(new { message = "Level 2a completed.", eventId = @event.EventId });
        })
        .WithSummary("Level 2a - Demonstrates OutboxMessageBuilder: WithTransaction, WithHeader, WithCorrelationId, WithCausationId");

        // ─── Endpoint 2b: WithTenantId() — multi-tenancy header enrichment ────
        // OutboxMessageBuilder.WithTenantId(tenantId) is a convenience shortcut that
        // adds the reserved header "x-tenant-id" to the message.
        // The broker publisher can use this header to route to tenant-specific topics/queues.
        // Equivalent to: .WithHeader("x-tenant-id", tenantId)
        app.MapPost("/api/level2/fluent-tenant", async (
            [FromServices] IOutbox outbox,
            [FromServices] NpgsqlDataSource dataSource,
            CancellationToken ct) =>
        {
            await using var conn = await dataSource.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            var @event = new OrderCreatedEvent(Guid.NewGuid(), "CUST-TENANT-A", 250m, DateTimeOffset.UtcNow);

            // WithTenantId() writes "x-tenant-id" header into the outbox message.
            // When dispatched, the broker publisher receives this header in OutboxMessageMetadata
            // and can use ITenantBrokerRouter to resolve the target topic/queue.
            await outbox.Publish(@event)
                .WithTransaction(tx.ToOutboxContext())
                .WithTenantId("tenant-acme")           // → adds header "x-tenant-id" = "tenant-acme"
                .WithCorrelationId("corr-tenant-demo")
                .StoreAsync(ct);
                
            await tx.CommitAsync(ct);

            return Results.Ok(new
            {
                message = "Level 2b: Message stored with WithTenantId().",
                explanation = new
                {
                    WithTenantId = "Shortcut for .WithHeader(\"x-tenant-id\", tenantId). Equivalent and idiomatic.",
                    Equivalent = ".WithHeader(\"x-tenant-id\", \"tenant-acme\")",
                    ReservedHeader = "\"x-tenant-id\" is the well-known header key read by ITenantBrokerRouter implementations.",
                }
            });
        })
        .WithSummary("Level 2b - WithTenantId(): multi-tenant message enrichment via reserved header")
        .WithTags("Level 2 — Configuration");

        // ─── Endpoint 2c: AddOutboxCleanupService() — soft-delete retention ───
        // When OutboxRuntimeOptions.DeleteOnDispatch = false (soft-delete mode),
        // dispatched messages are retained in the DB with status=2 (Dispatched).
        // AddOutboxCleanupService() registers a background worker that periodically
        // calls PurgeDispatchedMessagesAsync() to enforce the retention policy.
        //
        // This is a documentation/reference endpoint — the cleanup service is configured
        // at startup in Program.cs, not at request time.
        app.MapGet("/api/level2/cleanup-service-reference", () =>
        {
            return Results.Ok(new
            {
                description = "AddOutboxCleanupService() + OutboxCleanupOptions — automatic soft-delete retention.",
                requirement = "Only needed when OutboxRuntimeOptions.DeleteOnDispatch = false. " +
                    "In the default configuration (DeleteOnDispatch = true), dispatched messages are DELETED immediately and no cleanup is needed.",
                registrationInProgramCs = @"
// Step 1: Configure soft-delete mode:
services.AddOutbox(options =>
{
    options.ConfigureRuntimeOptions(runtime =>
    {
        runtime.DeleteOnDispatch = false; // Keep dispatched messages in DB for audit
    });
});

// Step 2: Register the cleanup service:
services.AddOutboxCleanupService(options =>
{
    options.Enabled = true;                          // Must be explicitly enabled
    options.RetentionPeriod = TimeSpan.FromDays(7); // Delete messages older than 7 days
    options.CleanupInterval = TimeSpan.FromHours(1); // Run cleanup every hour
    options.BatchSize = 1000;                        // Max rows per DELETE batch (avoids lock escalation)
});",
                outboxCleanupOptionsProperties = new[]
                {
                    "bool Enabled — must be true to activate the service (default: false, opt-in)",
                    "TimeSpan RetentionPeriod — messages dispatched earlier than (UtcNow - RetentionPeriod) are purged (default: 7 days)",
                    "TimeSpan CleanupInterval — interval between cleanup runs (default: 1 hour)",
                    "int BatchSize — max rows per DELETE to avoid table lock escalation (default: 1000)",
                },
                manualAlternative = "Use IOutboxRepository.PurgeDispatchedMessagesAsync(cutoff, batchSize, ct) " +
                    "from Level 11e for on-demand manual purging without the background service."
            });
        })
        .WithSummary("Level 2c - AddOutboxCleanupService() + OutboxCleanupOptions: soft-delete retention reference")
        .WithTags("Level 2 — Configuration");
    }
}


