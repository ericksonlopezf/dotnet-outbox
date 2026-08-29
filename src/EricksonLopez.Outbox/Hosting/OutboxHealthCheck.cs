// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox.Dispatcher;
using EricksonLopez.Outbox.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace EricksonLopez.Outbox.Hosting;

/// <summary>
/// Provides an ASP.NET Core Health Check implementation for the Outbox subsystem.
/// </summary>
/// <remarks>
/// <para>
/// This health check reports the current state of the dispatcher background service and the number
/// of pending messages in the outbox.
/// </para>
/// <para>
/// <b>Health status logic:</b>
/// <list type="bullet">
///   <item><description><b>Healthy</b>: Dispatcher is running, and pending messages are ≤ <see cref="OutboxHealthCheckOptions.WarningThreshold"/>.</description></item>
///   <item><description><b>Degraded</b>: Dispatcher is running but pending messages reach or exceed the threshold.</description></item>
///   <item><description><b>Unhealthy</b>: Dispatcher is not running, or retrieving the pending count failed.</description></item>
/// </list>
/// </para>
/// <para>
/// <b>Producer-only deployments:</b>
/// If no <see cref="OutboxDispatcherBackgroundService"/> is registered, the health check inherently
/// assumes a producer-only pattern. It will report <b>Healthy</b> and include a note that no dispatcher
/// is configured for this specific process.
/// </para>
/// </remarks>
[System.CLSCompliant(false)]

public sealed class OutboxHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOutboxRepository _repository;
    private readonly OutboxHealthCheckOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxHealthCheck"/> class.
    /// </summary>
    /// <param name="serviceProvider">The dependency injection container to optionally resolve the dispatcher.</param>
    /// <param name="repository">The repository used to query pending outbox messages.</param>
    /// <param name="options">The configured health check options, including warning thresholds.</param>
    public OutboxHealthCheck(
        IServiceProvider serviceProvider,
        IOutboxRepository repository,
        IOptions<OutboxHealthCheckOptions> options)
    {
        _serviceProvider = serviceProvider;
        _repository = repository;
        _options = options?.Value ?? new OutboxHealthCheckOptions();
    }

    /// <summary>
    /// Evaluates the health status of the outbox subsystem.
    /// </summary>
    /// <param name="context">The health check context.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the health check operation.</param>
    /// <returns>A task that represents the asynchronous health check operation, containing the <see cref="HealthCheckResult"/>.</returns>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // P1-FIX: Optional resolution — supports only-producer deployments.
        var dispatcher = _serviceProvider.GetService<OutboxDispatcherBackgroundService>();

        if (dispatcher == null)
        {
            // Only-producer pattern: no dispatcher registered — outbox is write-only from this process.
            // Report Healthy with informational data.
            return HealthCheckResult.Healthy(
                "Outbox is configured as producer-only (no dispatcher registered).",
                data: new Dictionary<string, object>
                {
                    ["dispatcher_state"] = "not_configured"
                });
        }

        if (!dispatcher.IsRunning)
        {
            return HealthCheckResult.Unhealthy(
                "Outbox dispatcher is not running.",
                data: new Dictionary<string, object>
                {
                    ["dispatcher_state"] = "stopped"
                });
        }

        long pendingCount;
        try
        {
            pendingCount = await _repository.GetPendingCountAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Failed to retrieve outbox pending message count.",
                exception: ex,
                data: new Dictionary<string, object>
                {
                    ["dispatcher_state"] = "running",
                    ["error"] = ex.Message
                });
        }

        var data = new Dictionary<string, object>
        {
            ["dispatcher_state"] = "running",
            ["pending_messages"] = pendingCount,
            ["warning_threshold"] = _options.WarningThreshold,
            ["storage_provider"] = _repository.GetType().Name
        };

        if (pendingCount >= _options.WarningThreshold)
        {
            return HealthCheckResult.Degraded(
                $"Outbox has {pendingCount} pending messages (threshold: {_options.WarningThreshold}).",
                data: data);
        }

        return HealthCheckResult.Healthy(
            $"Outbox dispatcher running. {pendingCount} pending messages.",
            data: data);
    }
}



