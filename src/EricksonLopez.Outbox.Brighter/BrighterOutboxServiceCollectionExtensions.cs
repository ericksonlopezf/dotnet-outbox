// Copyright © Erickson Lopez. MIT License.
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Paramore.Brighter;

namespace EricksonLopez.Outbox.Brighter;

/// <summary>
/// Provides extension methods for registering the outbox message producer for Brighter.
/// </summary>
public static class BrighterOutboxServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="OutboxMessageProducer"/> into the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The modified service collection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddOutboxBrighterProducer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IAmAMessageProducerAsync, OutboxMessageProducer>();
        return services;
    }
}

