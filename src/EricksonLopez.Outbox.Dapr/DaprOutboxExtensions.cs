// Copyright © Erickson Lopez. MIT License.
using System;
using Dapr.Client;
using EricksonLopez.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EricksonLopez.Outbox.Dapr;

/// <summary>
/// Provides extension methods for configuring Dapr Pub/Sub broker integration.
/// </summary>
public static class DaprOutboxExtensions
{
    /// <summary>
    /// Configures the outbox to use Dapr Pub/Sub as the default message broker.
    /// </summary>
    /// <param name="options">The outbox options.</param>
    /// <param name="pubsubName">The name of the Dapr pub/sub component.</param>
    /// <returns>The outbox options for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public static OutboxOptions UseDapr(
        this OutboxOptions options,
        string pubsubName = "pubsub")
    {
        ArgumentNullException.ThrowIfNull(options);

        options.UseBroker(sp =>
        {
            var client = sp.GetRequiredService<DaprClient>();
            return new DaprBrokerPublisher(client, pubsubName);
        });

        return options;
    }

    /// <summary>
    /// Registers the <see cref="DaprBrokerPublisher"/> as a broker publisher in the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="pubsubName">The name of the Dapr pub/sub component.</param>
    /// <returns>The service collection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddDaprBrokerPublisher(
        this IServiceCollection services,
        string pubsubName = "pubsub")
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IBrokerPublisher>(sp =>
        {
            var client = sp.GetRequiredService<DaprClient>();
            return new DaprBrokerPublisher(client, pubsubName);
        });

        return services;
    }
}

