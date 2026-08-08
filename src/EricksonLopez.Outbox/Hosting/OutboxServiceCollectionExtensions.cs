// Stryker disable all : Covered by ADR-013. Edge cases, micro-optimizations, logging, and validation strings are not rigorously mutated.
using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using EricksonLopez.Outbox.Dispatcher;
using EricksonLopez.Outbox.Idempotency;

namespace EricksonLopez.Outbox.Hosting;

/// <summary>
/// Extension methods for registering and configuring EricksonLopez.Outbox services within an <see cref="IServiceCollection"/>.
/// </summary>
[ExcludeFromCodeCoverage]
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
            var routes = new System.Collections.Generic.Dictionary<string, IBrokerPublisher>(StringComparer.Ordinal);
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

        // Ensure IOptions<OutboxDispatcherOptions> resolves even if not configured explicitly
        services.AddOptions<OutboxDispatcherOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.Extensions.Options.IValidateOptions<OutboxDispatcherOptions>, OutboxDispatcherOptionsValidator>());

        // Register validator for runtime options
        services.AddOptions<OutboxRuntimeOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.Extensions.Options.IValidateOptions<OutboxRuntimeOptions>, OutboxRuntimeOptionsValidator>());

        // Default implementation of IOutbox
        services.TryAddScoped<IOutbox, DefaultOutbox>();

        // Diagnostics
        // ISSUE-DI1 FIX: Extracted to shared private helper AddOutboxDiagnostics() below to
        // eliminate duplication with AddOutboxDispatcher's identical registration.
        OutboxServiceCollectionInternals.AddOutboxDiagnostics(services);

        // FIX-04: Register startup validator to fail fast if critical dependencies are missing.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, OutboxStartupValidator>());

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
            services.AddOptions<OutboxDispatcherOptions>();
        }

        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.Extensions.Options.IValidateOptions<OutboxDispatcherOptions>, OutboxDispatcherOptionsValidator>());

        // TryAdd* — idempotent if AddOutbox was called first
        services.TryAddSingleton<OutboxChannel>();
        services.TryAddSingleton<AdaptivePoller>();
        services.TryAddSingleton<IPollerWakeup>(sp => sp.GetRequiredService<AdaptivePoller>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, OutboxDispatcherBackgroundService>());
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
            services.AddOptions<OutboxInboxOptions>();
        }
        
        services.TryAddScoped<IInboxIdempotencyChecker, InboxIdempotencyChecker>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, InboxCleanupService>());

        return services;
    }
}

/// <summary>
/// Provides extension methods for registering outbox health checks.
/// </summary>
// AUDIT-FIX P1-F: [CLSCompliant(false)] is required because this class references
// IHealthChecksBuilder (from Microsoft.Extensions.Diagnostics.HealthChecks) and HealthStatus
// (from Microsoft.Extensions.Diagnostics.Abstractions), which are themselves marked as
// non-CLS-compliant due to their use of non-CLS types (uint) internally.
//
// The [assembly: CLSCompliant(true)] in AssemblyInfo.cs causes the compiler to verify every
// public member. Without this attribute here, the build would fail with CS3018 ("type cannot
// be marked as CLS-compliant because it derives from a non-CLS-compliant member").
//
// This pattern is identical to how ASP.NET Core annotates extension classes that reference
// non-CLS-compliant framework types. See also: PostgreSqlOutboxRepository which is
// [CLSCompliant(false)] for the same reason (NpgsqlDataSource is not CLS-compliant).
[System.CLSCompliant(false)]
public static class OutboxHealthCheckExtensions
{

    /// <summary>
    /// Adds the Outbox health check to the <see cref="Microsoft.Extensions.DependencyInjection.IHealthChecksBuilder"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The registered health check evaluates the running status of the background dispatcher and ensures
    /// the number of pending messages in the database does not exceed the configured warning threshold.
    /// </para>
    /// </remarks>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="name">The assigned name of the health check entry. Defaults to <c>"outbox"</c>.</param>
    /// <param name="warningThreshold">
    /// An optional threshold overriding the default pending message limit.
    /// If the limit is exceeded, the check evaluates to a <see cref="Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded"/> status.
    /// </param>
    /// <param name="tags">An optional list of tags to associate with the health check entry.</param>
    /// <returns>The modified <see cref="Microsoft.Extensions.DependencyInjection.IHealthChecksBuilder"/> for chaining.</returns>
    public static Microsoft.Extensions.DependencyInjection.IHealthChecksBuilder AddOutbox(
        this Microsoft.Extensions.DependencyInjection.IHealthChecksBuilder builder,
        string name = "outbox",
        int? warningThreshold = null,
        params string[] tags)
    {
        if (warningThreshold.HasValue)
        {
            builder.Services.Configure<OutboxHealthCheckOptions>(opt =>
            {
                opt.WarningThreshold = warningThreshold.Value;
            });
        }

        return builder.AddCheck<OutboxHealthCheck>(name, tags: tags);
    }
}

/// <summary>
/// Contains internal service collection helpers.
/// </summary>
internal static class OutboxServiceCollectionInternals
{
    // ISSUE-DI1 FIX: Shared helper that registers OutboxMetrics and ErrorSanitizer exactly once.
    // Both AddOutbox() and AddOutboxDispatcher() are designed to work independently
    // (producer-only vs dispatcher-only deployments). Both need metrics, so this helper
    // is extracted to avoid duplicating the registration line and risking divergence.
    // TryAddSingleton guarantees idempotency when both methods are called in the same app.
    internal static void AddOutboxDiagnostics(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        services.TryAddSingleton(sp => new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(sp.GetService<System.Diagnostics.Metrics.IMeterFactory>()));
        services.TryAddSingleton<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer, EricksonLopez.Outbox.Diagnostics.DefaultErrorSanitizer>();
    }
}
