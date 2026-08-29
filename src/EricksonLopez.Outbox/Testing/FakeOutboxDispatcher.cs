// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Result;

namespace EricksonLopez.Outbox.Testing;

/// <summary>
/// Provides a fake outbox dispatcher implementation for use in unit and integration tests.
/// </summary>
/// <remarks>
/// This dispatcher captures dispatch requests and provides fluent assertions to verify that the
/// correct messages were dispatched. It can be used to simulate the background dispatcher
/// behavior without requiring a running background service, verifying that the dispatcher
/// correctly invokes the configured broker publisher after messages are stored.
/// </remarks>
public sealed class FakeOutboxDispatcher
{
    private readonly FakeBrokerPublisher _broker;
    private readonly IOutboxRepository? _repository;
    private readonly List<OutboxMessage> _dispatchedMessages = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="FakeOutboxDispatcher"/> class.
    /// </summary>
    /// <param name="broker">The fake broker publisher that captures published messages.</param>
    /// <param name="repository">An optional repository that fetches pending messages and mark them as dispatched.</param>
    /// <exception cref="ArgumentNullException"><paramref name="broker"/> is <see langword="null"/>.</exception>
    public FakeOutboxDispatcher(
        FakeBrokerPublisher broker,
        IOutboxRepository? repository = null)
    {
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
        _repository = repository;
    }

    /// <summary>
    /// Manually triggers the dispatch process for all pending messages.
    /// </summary>
    /// <remarks>
    /// If an explicit list of <paramref name="messages"/> is provided, only those messages are dispatched.
    /// Otherwise, if a repository is configured, it fetches all pending messages from the repository and dispatches them.
    /// </remarks>
    /// <param name="messages">An optional explicit list of messages to dispatch.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the dispatch operation.</param>
    /// <returns>A task that represents the asynchronous dispatch operation. The task result contains the number of messages successfully dispatched.</returns>
    public async Task<int> DispatchAsync(
        IReadOnlyList<OutboxMessage>? messages = null,
        CancellationToken cancellationToken = default)
    {
        var toDispatch = messages;

        if (toDispatch is null && _repository is not null)
        {
            toDispatch = await _repository.FetchPendingAsync(int.MaxValue, cancellationToken);
        }

        if (toDispatch is null || toDispatch.Count == 0) return 0;

        var dispatched = 0;
        var batch = new List<OutboxMessage>(toDispatch.Count);

        foreach (var message in toDispatch)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var metadata = new OutboxMessageMetadata(
                correlationId: message.CorrelationId,
                causationId: message.CausationId,
                messageType: message.MessageType);

            var context = new DispatchContext(cancellationToken, attempt: 1);
            var result = await _broker.PublishRawAsync(message, metadata, context);

            if (result.Success)
            {
                _dispatchedMessages.Add(message);
                batch.Add(message);
                dispatched++;
            }
        }

        if (_repository != null)
        {
            await _repository.MarkAsDispatchedAsync(batch, cancellationToken);
        }

        return dispatched;
    }

    /// <summary>
    /// Gets a read-only list of all messages that were successfully dispatched during the test.
    /// </summary>
    public IReadOnlyList<OutboxMessage> DispatchedMessages => _dispatchedMessages;

    /// <summary>
    /// Clears the captured list of dispatched messages, resetting the state of the fake dispatcher.
    /// </summary>
    public void Reset() => _dispatchedMessages.Clear();

    /// <summary>
    /// Asserts that exactly the specified number of messages were successfully dispatched.
    /// </summary>
    /// <param name="count">The exact number of messages expected to have been dispatched.</param>
    /// <exception cref="InvalidOperationException">Thrown when the actual number of dispatched messages does not match the expected count.</exception>
    public void ShouldHaveDispatched(int count)
    {
        if (_dispatchedMessages.Count != count)
            throw new InvalidOperationException(
                $"Expected {count} message(s) to be dispatched, but {_dispatchedMessages.Count} were dispatched.");
    }

    /// <summary>
    /// Asserts that no messages were dispatched during the test.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when one or more messages were dispatched.</exception>
    public void ShouldHaveDispatchedNothing()
    {
        if (_dispatchedMessages.Count > 0)
            throw new InvalidOperationException(
                $"Expected no messages to be dispatched, but {_dispatchedMessages.Count} were dispatched.");
    }
}





