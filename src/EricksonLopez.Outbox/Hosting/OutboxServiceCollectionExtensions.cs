// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using EricksonLopez.Outbox.Dispatcher;
using EricksonLopez.Outbox.Idempotency;
using EricksonLopez.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace EricksonLopez.Outbox.Hosting;

/// <summary>
/// Extension methods for registering and configuring EricksonLopez.Outbox services within an <see cref="IServiceCollection"/>.
/// </summary>
public static class OutboxServiceCollectionExtensions
{
    /// <summary>
    /// Registers all core Outbox services, including the producer API and optional dispatcher components.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Is <b>idempotent</b>: it uses <c>TryAddSingleton</c> and <c>TryAddScoped</c>
    /// internally. Calling it multiple times or in combination with <see cref="AddOutboxDispatcher"/>
    /// will NOT result in duplicate service registrations.
    /// </para>
    /// <para>
    /// <b>Producer-only pattern:</b>
    /// Call <c>AddOutbox</c> without subsequently configuring the dispatcher if this application only writes
    /// messages to the outbox database, leaving the dispatch responsibility to a separate worker process.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to add the outbox services to.</param>
    /// <param name="configure">An optional action to configure the <see cref="OutboxOptions"/>.</param>
    /// <returns>The modified <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddOutbox(
        this IServiceCollection services,
        Action<OutboxOptions>? configure = null)
    {
        var options = new OutboxOptions(services);
        configure?.Invoke(options);

        services.TryAddSingleton<IBrokerSelector>(sp =>
        {
            var defaultPublisher = options.DefaultPublisherFactory?.Invoke(sp);
            var routes = new Dictionary<string, IBrokerPublisher>(StringComparer.Ordinal);
            foreach (var kvp in options.Routes)
            {
                routes[kvp.Key] = kvp.Value(sp);
            }
            return new EricksonLopez.Outbox.Dispatcher.DefaultBrokerSelector(defaultPublisher, routes);
        });

        if (options.DefaultPublisherFactory != null)
        {
            services.TryAddSingleton<IBrokerPublisher>(options.DefaultPublisherFactory);
        }

        // Ensure IOptions infrastructure and validators are registered
        services.AddOptions();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.Extensions.Options.IValidateOptions<OutboxDispatcherOptions>, OutboxDispatcherOptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.Extensions.Options.IValidateOptions<OutboxRuntimeOptions>, OutboxRuntimeOptionsValidator>());

        // Default implementation of IOutbox
        services.TryAddScoped<IOutbox, DefaultOutbox>();

        // Diagnostics
        // ISSUE-DI1 FIX: Extracted to shared private helper AddOutboxDiagnostics() below to
        // eliminate duplication with AddOutboxDispatcher's identical registration.
        OutboxServiceCollectionInternals.AddOutboxDiagnostics(services);

        // FIX-04: Register startup validator to fail fast if critical dependencies are missing.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, OutboxStartupValidator>());

        // Provide TimeProvider for deterministic testing
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }

    /// <summary>
    /// Explicitly registers and configures the Outbox dispatcher infrastructure and polling background services.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Is <b>idempotent</b> and safely co-exists with <see cref="AddOutbox"/>.
    /// </para>
    /// <para>
    /// Use when you need to provide specific configurations for the background dispatcher,
    /// such as max parallelism or batch sizes, distinct from the core outbox producer configuration.
    /// </para>
    /// <para>
    /// <b>Note on concurrency:</b> The outbox uses native database locking (e.g., <c>FOR UPDATE SKIP LOCKED</c>)
    /// to handle multi-instance concurrent polling safely and efficiently. Advanced mechanisms like
    /// <c>ILeaseManager</c> and distributed locks are completely optional and generally unnecessary
    /// unless you need strict leader-election topologies.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to add the dispatcher services to.</param>
    /// <param name="configure">An optional action to configure the <see cref="OutboxDispatcherOptions"/>.</param>
    /// <returns>The modified <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddOutboxDispatcher(
        this IServiceCollection services,
        Action<OutboxDispatcherOptions>? configure = null)
    {
        if (configure != null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions();
        }

        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.Extensions.Options.IValidateOptions<OutboxDispatcherOptions>, OutboxDispatcherOptionsValidator>());

        // TryAdd* — idempotent if AddOutbox was called first
        services.TryAddSingleton<OutboxChannel>();
        services.TryAddSingleton<AdaptivePoller>();
        services.TryAddSingleton<IPollerWakeup>(sp => sp.GetRequiredService<AdaptivePoller>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, OutboxDispatcherBackgroundService>());

        // Provide TimeProvider for deterministic testing
        services.TryAddSingleton(TimeProvider.System);

        // ISSUE-DI1 FIX: Share the diagnostics registration helper instead of duplicating the TryAddSingleton line.
        OutboxServiceCollectionInternals.AddOutboxDiagnostics(services);

        return services;
    }

    /// <summary>
    /// Registers the Inbox idempotency services, including the background cleanup service.
    /// </summary>
    /// <param name="services">The service collection to add the inbox services to.</param>
    /// <param name="configure">An optional action to configure the <see cref="OutboxInboxOptions"/>.</param>
    /// <returns>The modified <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddOutboxInbox(
        this IServiceCollection services,
        Action<OutboxInboxOptions>? configure = null)
    {
        if (configure != null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions();
        }

        services.TryAddScoped<IInboxIdempotencyChecker, InboxIdempotencyChecker>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, InboxCleanupService>());

        return services;
    }
}



