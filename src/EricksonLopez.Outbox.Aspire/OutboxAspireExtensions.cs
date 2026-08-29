// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace EricksonLopez.Outbox.Aspire;

/// <summary>
/// Provides extension methods for integrating the outbox engine with .NET Aspire host applications.
/// </summary>
public static class OutboxAspireExtensions
{
    private static readonly string[] OutboxHealthTags = ["ready", "live", "outbox"];

    /// <summary>
    /// Registers EricksonLopez.Outbox services into the .NET Aspire host builder, automatically
    /// configuring OpenTelemetry metrics, distributed tracing, and health checks.
    /// </summary>
    /// <param name="builder">The Aspire host application builder.</param>
    /// <param name="configure">Configuration delegate for outbox options.</param>
    /// <returns>The host application builder for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static IHostApplicationBuilder AddOutbox(
        this IHostApplicationBuilder builder,
        Action<OutboxOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // 1. Register Core Outbox Services
        builder.Services.AddOutbox(configure ?? (_ => { }));

        // 2. Register Health Checks
        builder.Services.AddHealthChecks()
            .AddCheck<OutboxHealthCheck>("outbox", HealthStatus.Unhealthy, OutboxHealthTags);

        return builder;
    }
}
