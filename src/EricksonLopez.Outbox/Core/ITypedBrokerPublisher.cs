// Copyright © Erickson Lopez. MIT License.
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EricksonLopez.Outbox;

/// <summary>
/// Defines a contract extending <see cref="IBrokerPublisher"/> to support strongly-typed message publishing.
/// </summary>
public interface ITypedBrokerPublisher : IBrokerPublisher
{
    /// <summary>
    /// Publishes a strongly-typed message envelope to the underlying broker.
    /// </summary>
    /// <typeparam name="T">The type of the message payload.</typeparam>
    /// <param name="message">The message envelope containing payload and metadata.</param>
    /// <param name="context">The context governing the dispatch operation.</param>
    /// <returns>A task representing the asynchronous operation, containing the <see cref="DispatchResult"/>.</returns>
    ValueTask<DispatchResult> PublishAsync<T>(
        MessageEnvelope<T> message,
        DispatchContext context) where T : notnull;

    /// <summary>
    /// Publishes a batch of strongly-typed message envelopes to the underlying broker.
    /// </summary>
    /// <typeparam name="T">The type of the message payload.</typeparam>
    /// <param name="messages">The read-only list of message envelopes to publish.</param>
    /// <param name="context">The context governing the dispatch operation.</param>
    /// <returns>A task representing the asynchronous operation, containing the list of dispatch results.</returns>
    ValueTask<IReadOnlyList<DispatchResult>> PublishBatchAsync<T>(
        IReadOnlyList<MessageEnvelope<T>> messages,
        DispatchContext context) where T : notnull;
}
