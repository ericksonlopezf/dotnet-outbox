// Copyright © Erickson Lopez. MIT License.
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Outbox.Pipeline;

/// <summary>
/// Defines a middleware component that participates in the outbox dispatch pipeline.
/// </summary>
public interface IOutboxMiddleware
{
    /// <summary>
    /// Invokes the middleware logic for the given message.
    /// </summary>
    /// <param name="message">The outbox message being processed.</param>
    /// <param name="metadata">The metadata associated with the outbox message.</param>
    /// <param name="next">The next delegate in the pipeline chain.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the middleware execution.</param>
    /// <returns>A task representing the asynchronous operation, containing the <see cref="DispatchResult"/>.</returns>
    ValueTask<DispatchResult> InvokeAsync(
        OutboxMessage message,
        OutboxMessageMetadata metadata,
        OutboxPipelineDelegate next,
        CancellationToken cancellationToken);
}
