// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace EricksonLopez.Outbox.Mediator;

/// <summary>
/// Provides extension methods for registering outbox mediator handlers with dependency injection.
/// </summary>
public static class OutboxMediatorServiceCollectionExtensions
{
    /// <summary>
    /// Registers an outbox notification handler for the specified <typeparamref name="TNotification"/> type.
    /// </summary>
    /// <typeparam name="TNotification">The notification type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddOutboxNotificationHandler<TNotification>(this IServiceCollection services)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<INotificationHandler<TNotification>, OutboxNotificationHandler<TNotification>>();
        return services;
    }
}
