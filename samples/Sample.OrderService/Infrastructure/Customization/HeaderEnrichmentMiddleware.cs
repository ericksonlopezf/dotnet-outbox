// Copyright © Erickson Lopez. MIT License.
using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Pipeline;

namespace Sample.OrderService.Infrastructure.Customization;

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
        OutboxMessageMetadata metadata,
        OutboxPipelineDelegate next,
        CancellationToken cancellationToken)
    {
        var enrichedEntries = new MetadataEntry[]
        {
            new("X-Dispatch-Host", Environment.MachineName),
            new("X-Dispatch-Timestamp", DateTimeOffset.UtcNow.ToString("O")),
            new("X-Retry-Count", message.RetryCount.ToString(CultureInfo.InvariantCulture)),
        };

        var enrichedMetadata = new OutboxMessageMetadata(
            metadata.CorrelationId,
            metadata.CausationId,
            metadata.MessageType,
            metadata.Entries.ToArray().Concat(enrichedEntries).ToArray());

        return next(message, enrichedMetadata, cancellationToken);
    }
}
