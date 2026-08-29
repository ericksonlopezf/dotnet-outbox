// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;

namespace EricksonLopez.Outbox;

/// <summary>
/// Configures multiple message-type routes to a designated broker publisher in bulk.
/// </summary>
public sealed class BrokerRouteGroupBuilder
{
    private readonly OutboxOptions _options;
    private readonly IReadOnlyList<string> _messageTypeAliases;

    internal BrokerRouteGroupBuilder(OutboxOptions options, IReadOnlyList<string> messageTypeAliases)
    {
        _options = options;
        _messageTypeAliases = messageTypeAliases;
    }

    /// <summary>
    /// Routes all message types in the group to the given singleton publisher instance.
    /// </summary>
    /// <param name="publisher">The publisher instance to dispatch messages to.</param>
    /// <returns>The parent <see cref="OutboxOptions"/> instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="publisher"/> is <see langword="null"/>.</exception>
    public OutboxOptions ToPublisher(IBrokerPublisher publisher)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        foreach (var alias in _messageTypeAliases)
        {
            if (!string.IsNullOrWhiteSpace(alias))
            {
                _options.Routes[alias] = _ => publisher;
            }
        }
        return _options;
    }

    /// <summary>
    /// Routes all message types in the group to a publisher resolved via the provided factory delegate.
    /// </summary>
    /// <param name="factory">A factory delegate that receives the <see cref="IServiceProvider"/> and returns the publisher to use.</param>
    /// <returns>The parent <see cref="OutboxOptions"/> instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    public OutboxOptions ToPublisher(Func<IServiceProvider, IBrokerPublisher> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        foreach (var alias in _messageTypeAliases)
        {
            if (!string.IsNullOrWhiteSpace(alias))
            {
                _options.Routes[alias] = factory;
            }
        }
        return _options;
    }
}
