using System;
using System.Diagnostics;
using System.Linq;
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
/// <remarks>
/// DI Registration (in Program.cs / Startup):
/// <code>
///   services.AddSingleton&lt;IOutboxMiddleware, TelemetryOutboxMiddleware&gt;();
/// </code>
/// The internal dispatch channel will resolve all <see cref="IOutboxMiddleware"/>
/// registered in DI and assemble them into the pipeline in registration order.
/// </remarks>
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
        MessageMetadata metadata,
        OutboxPipelineDelegate next,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            // ALWAYS call next() to continue the chain
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
            
            throw; // Let the dispatcher's interceptor catch it and map it to DispatchResult
        }
    }
}

/// <summary>
/// Showcase Middleware 2: Filtering
/// Demonstrates the "Short-Circuit" pattern.
/// Drops specific messages without sending them to the broker.
/// </summary>
/// <remarks>
/// Useful for dynamically discarding message types that are no longer supported
/// but are still stored in the DB as pending.
/// </remarks>
public sealed class MessageFilterMiddleware : IOutboxMiddleware
{
    private readonly ILogger<MessageFilterMiddleware> _logger;

    // Aliases of types that should be ignored (configurable in production from IOptions<>)
    private static readonly System.Collections.Frozen.FrozenSet<string> BlockedTypes =
        System.Collections.Frozen.FrozenSet.Create(StringComparer.OrdinalIgnoreCase, Array.Empty<string>());

    public MessageFilterMiddleware(ILogger<MessageFilterMiddleware> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public ValueTask<DispatchResult> InvokeAsync(
        OutboxMessage message,
        MessageMetadata metadata,
        OutboxPipelineDelegate next,
        CancellationToken cancellationToken)
    {
        // If the message type alias is in the block list...
        if (BlockedTypes.Contains(message.MessageType))
        {
            _logger.LogWarning("Message {MessageId} of type {Type} was blocked by filter.", 
                message.Id, message.MessageType);

            // SHORT-CIRCUIT: we don't call next().
            // We return Ok() so the dispatcher deletes it from the DB.
            return ValueTask.FromResult(DispatchResult.Ok());
        }

        // Pass-through for all others
        return next(message, metadata, cancellationToken);
    }
}

/// <summary>
/// Showcase Middleware 3: Header Enrichment
/// Demonstrates the "Enrich-and-Pass-Through" pattern.
/// Injects dynamic headers before passing the message to the broker.
/// </summary>
public sealed class HeaderEnrichmentMiddleware : IOutboxMiddleware
{
    /// <inheritdoc/>
    public ValueTask<DispatchResult> InvokeAsync(
        OutboxMessage message,
        MessageMetadata metadata,
        OutboxPipelineDelegate next,
        CancellationToken cancellationToken)
    {
        // Enriched headers: adds MachineName and current dispatch timestamp
        var enrichedEntries = new MetadataEntry[]
        {
            new("X-Dispatch-Host", Environment.MachineName),
            new("X-Dispatch-Timestamp", DateTimeOffset.UtcNow.ToString("O")),
            new("X-Retry-Count", message.RetryCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        };

        var enrichedMetadata = new MessageMetadata(
            metadata.CorrelationId,
            metadata.CausationId,
            metadata.MessageType,
            // Combines existing headers with the new ones
            metadata.Entries.ToArray().Concat(enrichedEntries).ToArray());

        // Calls next with the MODIFIED metadata
        return next(message, enrichedMetadata, cancellationToken);
    }
}
