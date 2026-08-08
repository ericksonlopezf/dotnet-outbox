using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Serialization;
using EricksonLopez.Outbox.Diagnostics;

namespace EricksonLopez.Outbox.Hosting;

/// <summary>
/// Provides the ability to manually dispatch messages instead of relying on the background poller.
/// </summary>
/// <remarks>
/// <para>
/// This service is particularly useful in serverless environments or for scenarios where dispatching
/// must be triggered via an external signal (e.g., a Web API call or Cron Job).
/// </para>
/// <para>
/// The dispatcher operates synchronously in its trigger but executes asynchronously.
/// </para>
/// </remarks>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed class ManualOutboxDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IBrokerPublisher _publisher;
    private readonly IOutboxMessageTypeResolver _typeResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManualOutboxDispatcher"/> class.
    /// </summary>
    /// <param name="serviceProvider">The dependency injection service provider.</param>
    /// <param name="publisher">The broker publisher responsible for transmitting messages.</param>
    /// <param name="typeResolver">The resolver used to map message aliases to concrete CLR types.</param>
    /// <exception cref="ArgumentNullException">Any of the provided arguments is <see langword="null"/>.</exception>
    public ManualOutboxDispatcher(
        IServiceProvider serviceProvider,
        IBrokerPublisher publisher,
        IOutboxMessageTypeResolver typeResolver)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _typeResolver = typeResolver ?? throw new ArgumentNullException(nameof(typeResolver));
    }

    /// <summary>
    /// Fetches up to the specified number of pending messages and dispatches them immediately.
    /// </summary>
    /// <param name="repository">The outbox repository from which pending messages are fetched.</param>
    /// <param name="batchSize">The maximum number of messages to fetch and dispatch in this run.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the number of messages successfully dispatched.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="repository"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="batchSize"/> is zero or negative.</exception>
    public async Task<int> DispatchPendingAsync(
        IOutboxRepository repository,
        int batchSize = 50,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        var messages = await repository.FetchPendingAsync(batchSize, cancellationToken);

        if (messages.Count == 0) return 0;

        var dispatched = new List<OutboxMessage>(messages.Count);

        foreach (var message in messages)
        {
            if (cancellationToken.IsCancellationRequested) break;

            // Resolve the CLR type from the stored alias
            var clrType = _typeResolver.Resolve(message.MessageType);
            if (clrType is null)
            {
                // Unknown type — mark as dead-letter to avoid infinite fetch-reclaim loops.
                await repository.MarkAsFailedAsync(
                    new[] { message }, 
                    $"Unknown message type: {message.MessageType}", 
                    isDeadLetter: true, 
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            var context = new DispatchContext(cancellationToken, attempt: 1);
            var metadata = new MessageMetadata(message.CorrelationId, message.CausationId, message.MessageType);

            // We publish as raw bytes via a non-generic overload to avoid runtime generics
            var result = await _publisher.PublishRawAsync(message, metadata, context);

            if (result.Success)
            {
                dispatched.Add(message);
            }
        }

        if (dispatched.Count > 0)
        {
            await repository.MarkAsDispatchedAsync(dispatched, cancellationToken);
        }

        return dispatched.Count;
    }
}
