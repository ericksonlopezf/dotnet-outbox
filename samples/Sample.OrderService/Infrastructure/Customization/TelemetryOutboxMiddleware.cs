// Copyright © Erickson Lopez. MIT License.
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Pipeline;
using Microsoft.Extensions.Logging;

namespace Sample.OrderService.Infrastructure.Customization;

/// <summary>
/// Showcase Middleware 1: Telemetry
/// Demonstrates the "Observe-and-Pass-Through" pattern.
/// Measures the exact execution time of a dispatch.
/// </summary>
public sealed class TelemetryOutboxMiddleware : IOutboxMiddleware
{
    private readonly ILogger<TelemetryOutboxMiddleware> _logger;

    public TelemetryOutboxMiddleware(ILogger<TelemetryOutboxMiddleware> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async ValueTask<DispatchResult> InvokeAsync(
        OutboxMessage message,
        OutboxMessageMetadata metadata,
        OutboxPipelineDelegate next,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await next(message, metadata, cancellationToken);

            sw.Stop();
            _logger.LogInformation(
                "Message {MessageId} dispatched in {ElapsedMs}ms. Success: {Success}",
                message.Id,
                sw.ElapsedMilliseconds,
                result.Success);

            return result;
        }
        catch (Exception)
        {
            sw.Stop();
            _logger.LogError(
                "Message {MessageId} failed unexpectedly during dispatch after {ElapsedMs}ms.",
                message.Id,
                sw.ElapsedMilliseconds);
            
            throw;
        }
    }
}
