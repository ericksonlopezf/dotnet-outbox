// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.Inbox.Configuration;
using EricksonLopez.Inbox.Core;
using EricksonLopez.Inbox.Hosting;
using EricksonLopez.Inbox.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EricksonLopez.Inbox;

/// <summary>
/// Provides extension methods for <see cref="IServiceCollection"/> to configure the Inbox deduplication and idempotency subsystem.
/// </summary>
public static class InboxServiceCollectionExtensions
{
    /// <summary>
    /// Adds core Inbox services to the specified <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration action for <see cref="InboxOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddInbox(
        this IServiceCollection services,
        Action<InboxOptions>? configure = null)
    {
        // Stryker disable once Statement : Defensive guard clause; downstream AddOptions also validates services
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services.AddOptions<InboxOptions>();
        if (configure != null)
        {
            optionsBuilder.Configure(configure);
        }

        services.TryAddSingleton<IInboxConsumerFilter, DefaultInboxConsumerFilter>();
        services.TryAddSingleton<IIdempotencyChecker, IdempotencyChecker>();
        services.AddHostedService<InboxCleanupBackgroundService>();

        return services;
    }

    /// <summary>
    /// Configures the Inbox to use the thread-safe <see cref="InMemoryInboxStore"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration action for <see cref="InboxOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddInMemoryInbox(
        this IServiceCollection services,
        Action<InboxOptions>? configure = null)
    {
        // Stryker disable once Statement : Defensive guard clause; downstream AddInbox also validates services
        ArgumentNullException.ThrowIfNull(services);

        services.AddInbox(configure);
        services.TryAddSingleton<IInboxStore, InMemoryInboxStore>();

        return services;
    }
}
