// Copyright © Erickson Lopez. MIT License.
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Outbox.Pipeline;

/// <summary>
/// Represents a single step in the outbox dispatch pipeline.
/// </summary>
/// <param name="message">The outbox message being processed.</param>
/// <param name="metadata">The metadata associated with the outbox message.</param>
/// <param name="cancellationToken">A token that can be used to cancel the pipeline execution.</param>
/// <returns>A task representing the asynchronous operation, containing the <see cref="DispatchResult"/>.</returns>
public delegate ValueTask<DispatchResult> OutboxPipelineDelegate(
    OutboxMessage message,
    OutboxMessageMetadata metadata,
    CancellationToken cancellationToken);
