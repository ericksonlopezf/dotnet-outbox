using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Sample.OrderService.Domain.Aggregates.OrderAggregate;
using Sample.OrderService.Infrastructure;

using Microsoft.EntityFrameworkCore.Storage;

namespace Sample.OrderService.Endpoints;

/// <summary>
/// Level 4 — Advanced Integration
/// Transaction boundaries and Entity Framework Core
/// </summary>
public static class Level4_AdvancedIntegrationEndpoints
{
    public static void MapLevel4AdvancedIntegration(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/level4/ef-core", async (
            [FromServices] AppDbContext dbContext,
            [FromServices] IOutbox outbox,
            CancellationToken ct) =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

            // 1. Domain
            var order = Order.Create("CUST-EF", 200m);
            dbContext.Orders.Add(order);
            await dbContext.SaveChangesAsync(ct);

            // 2. Outbox using DbTransactionContext to extract DbTransaction
            var @event = new OrderCreatedEvent(order.Id, order.CustomerId, order.Total, order.CreatedAt);
            await outbox.StoreAsync(@event, new EricksonLopez.Outbox.Persistence.DbTransactionContext(transaction.GetDbTransaction()), ct);

            // 3. Unified commit
            await transaction.CommitAsync(ct);

            return Results.Ok(new { message = "Level 4 completed." });
        })
        .WithSummary("Level 4 - Entity Framework Core Transaction Boundary");
    }
}

