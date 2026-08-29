// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sample.OrderService.Application.Orders.Commands.CreateOrder;
using System.Threading.Tasks;

namespace Sample.OrderService.Endpoints;

/// <summary>
/// Level 3 — Real Use Cases
/// Demonstrates real usage within Clean Architecture (MediatR Handlers).
/// </summary>
public static class Level3_RealUseCasesEndpoints
{
    public static void MapLevel3RealUseCases(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/level3/clean-architecture", async (
            [FromBody] CreateOrderCommand command,
            [FromServices] IMediator mediator,
            CancellationToken ct) =>
        {
            // The MediatR handler manages the DB and the IOutbox
            var orderId = await mediator.Send(command, ct);
            return Results.Ok(new { message = "Level 3 completed.", orderId });
        })
        .WithSummary("Level 3 - Clean Architecture via MediatR");
    }
}



