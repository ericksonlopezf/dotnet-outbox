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

namespace Sample.OrderService.Endpoints;

/// <summary>
/// Level 1 — Quick Start
/// Demonstrates installation, minimal configuration, and dependency injection.
/// </summary>
public static class Level1_QuickStartEndpoints
{
    public static void MapLevel1QuickStart(this IEndpointRouteBuilder app)
    {
        // ─── Endpoint 1a: Basic StoreAsync ──────────────────────────────────
        // Shows the direct use of IOutbox.StoreAsync<TMessage> with DbTransactionContext.
        // DbTransactionContext is the standard adapter to wrap an ADO.NET DbTransaction.
        app.MapPost("/api/level1/quickstart", async (
            [FromBody] StoreRequest request,
            [FromServices] IOutbox outbox,
            [FromServices] NpgsqlDataSource dataSource,
            CancellationToken ct) =>
        {
            await using var conn = await dataSource.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            var @event = new OrderCreatedEvent(Guid.NewGuid(), request.CustomerId, request.Total, DateTimeOffset.UtcNow);

            // First functional use: Atomic StoreAsync with ADO.NET transaction.
            // DbTransactionContext(tx) wraps the DbTransaction into an IOutboxTransactionContext.
            await outbox.StoreAsync(@event, new DbTransactionContext(tx), ct);

            await tx.CommitAsync(ct);

            return Results.Ok(new { message = "Level 1a: Basic StoreAsync() completed.", eventId = @event.EventId });
        })
        .WithSummary("Level 1a - IOutbox.StoreAsync() with DbTransactionContext")
        .WithTags("Level 1 — Quick Start");

        // ─── Endpoint 1b: ToOutboxContext() Extension Method ─────────────────
        // Demonstrates OutboxTransactionContextExtensions.ToOutboxContext()
        // which fluently converts a DbTransaction into an IOutboxTransactionContext.
        //
        // Equivalent to endpoint 1a, but using the extension method as syntactic sugar.
        // Reduces boilerplate: tx.ToOutboxContext() instead of new DbTransactionContext(tx).
        app.MapPost("/api/level1/quickstart-ext", async (
            [FromBody] StoreRequest request,
            [FromServices] IOutbox outbox,
            [FromServices] NpgsqlDataSource dataSource,
            CancellationToken ct) =>
        {
            await using var conn = await dataSource.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            var @event = new OrderConfirmedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow);

            // ToOutboxContext() is the convenience extension method on DbTransaction.
            // Namespace: EricksonLopez.Outbox.Persistence.OutboxTransactionContextExtensions
            await outbox.StoreAsync(@event, tx.ToOutboxContext(), ct);

            await tx.CommitAsync(ct);

            return Results.Ok(new { message = "Level 1b: ToOutboxContext() completed.", eventId = @event.EventId });
        })
        .WithSummary("Level 1b - Extension Method: tx.ToOutboxContext()")
        .WithTags("Level 1 — Quick Start");
    }

    public record StoreRequest(string CustomerId, decimal Total);
}
