// Copyright © Erickson Lopez. MIT License.
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Events.Contracts;
using EricksonLopez.Events.Envelopes;
using EricksonLopez.Inbox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EricksonLopez.Outbox.Inbox.Events;

/// <summary>
/// Decorates an <see cref="IEventHandler{TEvent}"/> with idempotent execution guarantees backed by <see cref="IInboxConsumerFilter"/>.
/// </summary>
/// <typeparam name="TEvent">The event type.</typeparam>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Implements IEventHandler decoration pattern.")]
public sealed class IdempotentEventHandler<TEvent> : IEventHandler<TEvent>
    where TEvent : IEvent
{
    private static readonly Action<ILogger, string, string, string, Exception?> EventSkippedLog =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Debug,
            new EventId(1, nameof(IdempotentEventHandler<TEvent>)),
            "Event '{EventType}' with ID '{EventId}' was skipped as a duplicate by consumer '{ConsumerName}'.");

    private readonly IEventHandler<TEvent> _innerHandler;
    private readonly IInboxConsumerFilter _inboxFilter;
    private readonly string _consumerName;
    private readonly ILogger<IdempotentEventHandler<TEvent>> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotentEventHandler{TEvent}"/> class with the specified inner handler and inbox filter.
    /// </summary>
    /// <param name="innerHandler">The inner business event handler.</param>
    /// <param name="inboxFilter">The inbox consumer filter for deduplication.</param>
    /// <param name="consumerName">The optional logical consumer name.</param>
    /// <param name="logger">The optional logger instance.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerHandler"/> or <paramref name="inboxFilter"/> is <see langword="null"/></exception>
    public IdempotentEventHandler(
        IEventHandler<TEvent> innerHandler,
        IInboxConsumerFilter inboxFilter,
        string? consumerName = null,
        ILogger<IdempotentEventHandler<TEvent>>? logger = null)
    {
        _innerHandler = innerHandler ?? throw new ArgumentNullException(nameof(innerHandler));
        _inboxFilter = inboxFilter ?? throw new ArgumentNullException(nameof(inboxFilter));
        _consumerName = consumerName ?? innerHandler.GetType().FullName!;
        _logger = logger ?? NullLogger<IdempotentEventHandler<TEvent>>.Instance;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="eventInstance"/> is <see langword="null"/></exception>
    public async ValueTask HandleAsync(TEvent eventInstance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventInstance);

        // Derive deterministic message ID from event envelope or identity
        var messageId = (eventInstance as IEventEnvelope)?.Id.ToString()
                        ?? $"{typeof(TEvent).Name}:{eventInstance.GetHashCode()}";

        var handled = await _inboxFilter.ExecuteIdempotentlyAsync(
            messageId: messageId,
            consumerName: _consumerName,
            handler: ct => _innerHandler.HandleAsync(eventInstance, ct),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!handled)
        {
            EventSkippedLog(_logger, typeof(TEvent).Name, messageId, _consumerName, null);
        }
    }
}
