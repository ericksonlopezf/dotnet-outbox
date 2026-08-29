// Copyright © Erickson Lopez. MIT License.
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EricksonLopez.Outbox.Inbox;

/// <summary>
/// Provides extension methods for registering inbox consumer idempotency services.
/// </summary>
public static class InboxConsumerRegistrationExtensions
{
    /// <summary>
    /// Registers the <see cref="InboxConsumerFilter"/> into the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The modified service collection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddInboxDeduplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IInboxConsumerFilter, InboxConsumerFilter>();
        return services;
    }
}

