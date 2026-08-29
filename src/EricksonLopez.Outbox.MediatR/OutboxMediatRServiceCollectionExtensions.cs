// Copyright © Erickson Lopez. MIT License.
using System;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EricksonLopez.Outbox.MediatR;

/// <summary>
/// Provides extension methods for configuring the MediatR outbox publisher integration.
/// </summary>
public static class OutboxMediatRServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="OutboxNotificationPublisher"/> as the primary MediatR notification publisher.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The modified service collection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddOutboxMediatRPublisher(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.RemoveAll<INotificationPublisher>();
        services.AddScoped<INotificationPublisher, OutboxNotificationPublisher>();

        return services;
    }
}

