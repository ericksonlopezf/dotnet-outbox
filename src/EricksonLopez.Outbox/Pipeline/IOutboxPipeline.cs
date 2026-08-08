using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;

namespace EricksonLopez.Outbox.Pipeline;

/// <summary>
/// Represents a single step in the outbox dispatch pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Middleware components can observe, mutate, enrich, or short-circuit the dispatch flow.
/// </para>
/// <para>
/// <b>Design rationale:</b> Using a delegate chain (similar to ASP.NET Core middleware) results in
/// zero-allocation compared to reflection-based or object-based pipelines. Each step receives the
/// context and a delegate to the next step.
/// </para>
/// </remarks>
/// <param name="message">The outbox message currently being processed.</param>
/// <param name="metadata">The metadata associated with the outbox message.</param>
/// <param name="cancellationToken">A token that can be used to cancel the pipeline execution.</param>
/// <returns>A task that represents the asynchronous pipeline execution, yielding a <see cref="DispatchResult"/>.</returns>
public delegate ValueTask<DispatchResult> OutboxPipelineDelegate(
    OutboxMessage message,
    MessageMetadata metadata,
    CancellationToken cancellationToken);

/// <summary>
/// Defines a middleware component that participates in the outbox dispatch pipeline.
/// </summary>
public interface IOutboxMiddleware
{
    /// <summary>
    /// Invokes the middleware logic for the given message.
    /// </summary>
    /// <remarks>
    /// Call <paramref name="next"/> to advance to the next step in the pipeline.
    /// Omitting the call to <paramref name="next"/> effectively short-circuits the pipeline,
    /// preventing subsequent middleware from executing.
    /// </remarks>
    /// <param name="message">The outbox message currently being processed.</param>
    /// <param name="metadata">The metadata associated with the outbox message.</param>
    /// <param name="next">The delegate representing the remainder of the pipeline.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the pipeline execution.</param>
    /// <returns>A task that represents the asynchronous middleware execution, yielding a <see cref="DispatchResult"/>.</returns>
    ValueTask<DispatchResult> InvokeAsync(
        OutboxMessage message,
        MessageMetadata metadata,
        OutboxPipelineDelegate next,
        CancellationToken cancellationToken);
}

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

        // Materialize once into an array. A single allocation here beats a List<T>
        // allocation in the caller (OutboxChannel) on every batch.
        // Stryker disable all : Array cast micro-optimization
        var mw = middlewares as IOutboxMiddleware[] ?? System.Linq.Enumerable.ToArray(middlewares);
        // Stryker restore all

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
        MessageMetadata metadata,
        CancellationToken cancellationToken)
    {
        return _pipelineChain(message, metadata, cancellationToken);
    }
}
