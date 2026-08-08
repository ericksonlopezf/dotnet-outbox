using System;
using System.Collections.Generic;

namespace EricksonLopez.Outbox.Dispatcher;

/// <summary>
/// Default implementation of <see cref="IBrokerSelector"/> that routes messages to specific publishers based on message type.
/// </summary>
internal sealed class DefaultBrokerSelector : IBrokerSelector
{
    private readonly IBrokerPublisher? _defaultPublisher;
    private readonly IReadOnlyDictionary<string, IBrokerPublisher> _routes;

    public DefaultBrokerSelector(IBrokerPublisher? defaultPublisher, IReadOnlyDictionary<string, IBrokerPublisher>? routes = null)
    {
        _defaultPublisher = defaultPublisher;
        _routes = routes ?? new Dictionary<string, IBrokerPublisher>();
    }

    public IBrokerPublisher GetPublisher(OutboxMessage message)
    {
        if (_routes.TryGetValue(message.MessageType, out var publisher))
        {
            return publisher;
        }

        if (_defaultPublisher != null)
        {
            return _defaultPublisher;
        }

        throw new InvalidOperationException($"No broker publisher configured for message type '{message.MessageType}' and no default publisher is registered.");
    }
}
