// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Outbox;

/// <summary>
/// Configures a message-type-specific route to a designated broker publisher.
/// </summary>
public sealed class BrokerRouteBuilder
{
    private readonly OutboxOptions _options;
    private readonly string _messageTypeAlias;

    internal BrokerRouteBuilder(OutboxOptions options, string messageTypeAlias)
    {
        _options = options;
        _messageTypeAlias = messageTypeAlias;
    }

    /// <summary>
    /// Routes the specified message type to the given singleton publisher instance.
    /// </summary>
    /// <param name="publisher">The publisher instance to dispatch messages of the configured type to.</param>
    /// <returns>The parent <see cref="OutboxOptions"/> instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="publisher"/> is <see langword="null"/>.</exception>
    public OutboxOptions ToPublisher(IBrokerPublisher publisher)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        _options.Routes[_messageTypeAlias] = _ => publisher;
        return _options;
    }

    /// <summary>
    /// Routes the specified message type to a publisher resolved via the provided factory delegate.
    /// </summary>
    /// <param name="factory">A factory delegate that receives the <see cref="IServiceProvider"/> and returns the publisher to use.</param>
    /// <returns>The parent <see cref="OutboxOptions"/> instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    public OutboxOptions ToPublisher(Func<IServiceProvider, IBrokerPublisher> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _options.Routes[_messageTypeAlias] = factory;
        return _options;
    }
}
