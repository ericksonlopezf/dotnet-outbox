// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Outbox.Pipeline;

/// <summary>
/// Builds and executes an immutable chain of <see cref="IOutboxMiddleware"/> components.
/// </summary>
public sealed class OutboxPipeline
{
    private readonly OutboxPipelineDelegate _pipelineChain;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxPipeline"/> class.
    /// </summary>
    /// <param name="middlewares">The sequence of middleware components to include in the pipeline.</param>
    /// <param name="terminal">The final delegate to execute at the end of the middleware chain.</param>
    /// <exception cref="ArgumentNullException"><paramref name="middlewares"/> or <paramref name="terminal"/> is <see langword="null"/>.</exception>
    public OutboxPipeline(
        IEnumerable<IOutboxMiddleware> middlewares,
        OutboxPipelineDelegate terminal)
    {
        ArgumentNullException.ThrowIfNull(middlewares);
        ArgumentNullException.ThrowIfNull(terminal);

        var mw = middlewares.ToArray();

        OutboxPipelineDelegate current = terminal;
        for (int i = mw.Length - 1; i >= 0; i--)
        {
            var middleware = mw[i];
            var next = current;
            current = (msg, meta, ct) => middleware.InvokeAsync(msg, meta, next, ct);
        }
        _pipelineChain = current;
    }

    /// <summary>
    /// Executes the pre-built, fully assembled middleware chain for the given message.
    /// </summary>
    /// <param name="message">The outbox message to process through the pipeline.</param>
    /// <param name="metadata">The metadata associated with the outbox message.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the pipeline execution.</param>
    /// <returns>A task that represents the asynchronous pipeline execution, yielding the final <see cref="DispatchResult"/>.</returns>
    public ValueTask<DispatchResult> ExecuteAsync(
        OutboxMessage message,
        OutboxMessageMetadata metadata,
        CancellationToken cancellationToken)
    {
        return _pipelineChain(message, metadata, cancellationToken);
    }
}
