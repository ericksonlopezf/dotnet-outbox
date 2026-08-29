// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.Outbox.Diagnostics;
using EricksonLopez.Outbox.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace EricksonLopez.Outbox;

/// <summary>
/// Provides extension methods for registering outbox health checks.
/// </summary>
[CLSCompliant(false)]
public static class OutboxHealthCheckExtensions
{
    /// <summary>
    /// Adds the Outbox health check to the <see cref="IHealthChecksBuilder"/>.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="name">The name of the health check registration. Default is <c>"outbox"</c>.</param>
    /// <param name="warningThreshold">The optional warning threshold for pending messages before degrading health status.</param>
    /// <param name="tags">Optional tags used to filter health checks.</param>
    /// <returns>The modified <see cref="IHealthChecksBuilder"/> for method chaining.</returns>
    public static IHealthChecksBuilder AddOutbox(
        this IHealthChecksBuilder builder,
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

    /// <summary>
    /// Registers the <see cref="OutboxCleanupService"/> background worker to purge old dispatched outbox messages.
    /// </summary>
    /// <param name="services">The service collection to register the service into.</param>
    /// <param name="configure">An optional action to configure <see cref="OutboxCleanupOptions"/>.</param>
    /// <returns>The modified <see cref="IServiceCollection"/> for method chaining.</returns>
    public static IServiceCollection AddOutboxCleanupService(
        this IServiceCollection services,
        Action<OutboxCleanupOptions>? configure = null)
    {
        var optionsBuilder = services.AddOptions<OutboxCleanupOptions>();
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }
        else
        {
            optionsBuilder.Configure(opt => opt.Enabled = true);
        }

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, OutboxCleanupService>());
        return services;
    }
}
