// Copyright © Erickson Lopez. MIT License.
using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EricksonLopez.Outbox.Inbox.AspNetCore;

/// <summary>
/// Provides extension methods for registering and applying HTTP idempotency in ASP.NET Core pipelines.
/// </summary>
public static class InboxAspNetCoreExtensions
{
    /// <summary>
    /// Enforces idempotency tracking on this endpoint via the <c>Idempotency-Key</c> header.
    /// </summary>
    /// <param name="builder">The route handler builder.</param>
    /// <param name="headerName">The idempotency header name.</param>
    /// <returns>The route handler builder for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static RouteHandlerBuilder RequireIdempotency(this RouteHandlerBuilder builder, string headerName = "Idempotency-Key")
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddEndpointFilter(new IdempotentEndpointFilter(headerName));
        return builder;
    }

    /// <summary>
    /// Enforces idempotency tracking on this route group via the <c>Idempotency-Key</c> header.
    /// </summary>
    /// <param name="builder">The route group builder.</param>
    /// <param name="headerName">The idempotency header name.</param>
    /// <returns>The route group builder for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static RouteGroupBuilder RequireIdempotency(this RouteGroupBuilder builder, string headerName = "Idempotency-Key")
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddEndpointFilter(new IdempotentEndpointFilter(headerName));
        return builder;
    }
}
