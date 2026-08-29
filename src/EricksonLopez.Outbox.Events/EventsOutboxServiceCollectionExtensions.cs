// Copyright © Erickson Lopez. MIT License.
using System;
using System.Diagnostics.CodeAnalysis;
using EricksonLopez.Events.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EricksonLopez.Outbox.Events;

/// <summary>
/// Provides extension methods for registering <see cref="OutboxEventPublisher"/> with dependency injection.
/// </summary>
public static class EventsOutboxServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="OutboxEventPublisher"/> as the implementation for <see cref="IEventPublisher"/>.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to configure.</param>
    /// <returns>The configured <see cref="IServiceCollection"/> instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/></exception>
    public static IServiceCollection AddOutboxEventPublisher(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IOutboxTransactionProvider, NullOutboxTransactionProvider>();
        services.AddScoped<IEventPublisher, OutboxEventPublisher>();
        return services;
    }

    /// <summary>
    /// Registers the <see cref="OutboxEventPublisher"/> along with a custom <typeparamref name="TTransactionProvider"/>.
    /// </summary>
    /// <typeparam name="TTransactionProvider">The type of the custom transaction provider.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to configure.</param>
    /// <returns>The configured <see cref="IServiceCollection"/> instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/></exception>
    public static IServiceCollection AddOutboxEventPublisher<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TTransactionProvider>(this IServiceCollection services)
        where TTransactionProvider : class, IOutboxTransactionProvider
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IOutboxTransactionProvider, TTransactionProvider>();
        services.AddScoped<IEventPublisher, OutboxEventPublisher>();
        return services;
    }
}
