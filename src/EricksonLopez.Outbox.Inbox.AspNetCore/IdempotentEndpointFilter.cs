// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace EricksonLopez.Outbox.Inbox.AspNetCore;

/// <summary>
/// Provides a minimal API filter that enforces and processes HTTP requests idempotently via the <c>Idempotency-Key</c> header.
/// </summary>
public sealed class IdempotentEndpointFilter : IEndpointFilter
{
    private readonly string _headerName;

    /// <summary>
    /// Initializes a new instance of <see cref="IdempotentEndpointFilter"/>.
    /// </summary>
    /// <param name="headerName">The idempotency header name (default: <c>Idempotency-Key</c>).</param>
    public IdempotentEndpointFilter(string headerName = "Idempotency-Key")
    {
        _headerName = string.IsNullOrWhiteSpace(headerName) ? "Idempotency-Key" : headerName;
    }

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        if (!httpContext.Request.Headers.TryGetValue(_headerName, out var keyValues) || string.IsNullOrWhiteSpace(keyValues))
        {
            return await next(context).ConfigureAwait(false);
        }

        var inboxFilter = httpContext.RequestServices.GetService<IInboxConsumerFilter>();
        if (inboxFilter is null)
        {
            // If inbox filter is not registered, continue processing
            return await next(context).ConfigureAwait(false);
        }

        var idempotencyKey = keyValues.ToString();
        var endpointName = string.IsNullOrEmpty(httpContext.Request.Path.Value) ? "HttpEndpoint" : httpContext.Request.Path.Value;

        object? result = null;
        var handled = await inboxFilter.ExecuteIdempotentlyAsync(
            messageId: idempotencyKey,
            consumerName: endpointName,
            handler: async ct =>
            {
                result = await next(context).ConfigureAwait(false);
            },
            cancellationToken: httpContext.RequestAborted).ConfigureAwait(false);

        if (!handled && result is null)
        {
            return Results.Conflict(new
            {
                Error = "IdempotentRequestAlreadyProcessed",
                Message = $"A request with Idempotency-Key '{idempotencyKey}' was already processed or is currently in-flight."
            });
        }

        return result;
    }
}
