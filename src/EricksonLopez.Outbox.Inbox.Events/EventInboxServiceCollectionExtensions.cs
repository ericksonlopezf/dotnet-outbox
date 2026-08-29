// Copyright © Erickson Lopez. MIT License.
using System;
using System.Diagnostics.CodeAnalysis;
using EricksonLopez.Events.Contracts;
using EricksonLopez.Inbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EricksonLopez.Outbox.Inbox.Events;

/// <summary>
/// Provides extension methods for registering idempotent event handling decorators in an <see cref="IServiceCollection"/>.
/// </summary>
public static class EventInboxServiceCollectionExtensions
{
    /// <summary>
    /// Decorates a registered <see cref="IEventHandler{TEvent}"/> with idempotent inbox execution.
    /// </summary>
    /// <typeparam name="TEvent">The type of the event to handle.</typeparam>
    /// <typeparam name="THandler">The handler implementation type.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to configure.</param>
    /// <param name="consumerName">The optional consumer name override.</param>
    /// <returns>The configured <see cref="IServiceCollection"/> instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/></exception>
    public static IServiceCollection AddIdempotentEventHandler<TEvent, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(
        this IServiceCollection services,
        string? consumerName = null)
        where TEvent : class, IEvent
        where THandler : class, IEventHandler<TEvent>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<THandler>();
        services.AddScoped<IEventHandler<TEvent>>(sp =>
        {
            var inner = sp.GetRequiredService<THandler>();
            var filter = sp.GetRequiredService<IInboxConsumerFilter>();
            var logger = sp.GetService<ILogger<IdempotentEventHandler<TEvent>>>();
            return new IdempotentEventHandler<TEvent>(inner, filter, consumerName, logger);
        });

        return services;
    }
}
