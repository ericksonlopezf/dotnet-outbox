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
/// Level 2 — Configuration and Fluent API
/// Demonstrates the OutboxMessageBuilder for detailed message configuration.
/// </summary>
public static class Level2_ConfigurationEndpoints
{
    public static void MapLevel2Configuration(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/level2/fluent", async (
            [FromServices] IOutbox outbox,
            [FromServices] NpgsqlDataSource dataSource,
            CancellationToken ct) =>
        {
            await using var conn = await dataSource.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            var @event = new OrderCreatedEvent(Guid.NewGuid(), "CUST-1", 100m, DateTimeOffset.UtcNow);
            
            // Full configuration with Message Builder (Fluent API)
            await outbox.Publish(@event)
                .WithHeader("X-Correlation-Id", Guid.NewGuid().ToString())
                .WithCausationId("cause-id-123")
                .WithCorrelationId("corr-id-123")
                .WithTransaction(tx.ToOutboxContext())
                .StoreAsync(ct);
                
            await tx.CommitAsync(ct);

            return Results.Ok(new { message = "Level 2 completed.", eventId = @event.EventId });
        })
        .WithSummary("Level 2 - Demonstrates OutboxMessageBuilder");
    }
}
