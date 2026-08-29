// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Diagnostics;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EricksonLopez.Outbox.Hosting;

/// <summary>
/// Validates that critical Outbox dependencies are correctly registered in the DI container
/// at application startup, failing fast to prevent cryptic runtime errors.
/// </summary>
/// <remarks>
/// <para>
/// <b>Checks performed:</b>
/// <list type="bullet">
///   <item><description><see cref="IOutboxSerializer"/> — required for serialization of all messages.</description></item>
///   <item><description><see cref="IOutboxMessageTypeResolver"/> — required for alias resolution.</description></item>
///   <item><description><see cref="IOutboxRepository"/> — required for DB persistence.</description></item>
///   <item><description><see cref="EricksonLopez.Outbox.Dispatcher.OutboxDispatcherBackgroundService"/> — optional (warns if running in producer-only mode).</description></item>
/// </list>
/// </para>
/// <para>
/// This background service executes synchronously during application initialization.
/// If any required service is missing, it throws an <see cref="InvalidOperationException"/>
/// detailing the missing dependencies, which halts application startup.
/// </para>
/// </remarks>
internal sealed class OutboxStartupValidator : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OutboxStartupValidator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxStartupValidator"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider to interrogate for registered dependencies.</param>
    /// <param name="logger">The logger that records validation outcomes.</param>
    public OutboxStartupValidator(IServiceProvider serviceProvider, ILogger<OutboxStartupValidator> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Executes the validation checks synchronously during application startup.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token that indicates when the start process should abort.</param>
    /// <returns>A completed task if validation succeeds.</returns>
    /// <exception cref="InvalidOperationException">One or more critical dependencies are missing from the DI container.</exception>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        ValidateCriticalDependencies();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Performs no operation; provided to satisfy the <see cref="IHostedService"/> contract.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token that indicates when the stop process should abort.</param>
    /// <returns>A completed task.</returns>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void ValidateCriticalDependencies()
    {
        var errors = new List<string>();

        // IOutboxSerializer - required for all message storage paths.
        if (_serviceProvider.GetService<IOutboxSerializer>() is null)
        {
            errors.Add(
                "IOutboxSerializer is not registered. " +
                "Call options.UseSerializer(...) or options.UseGeneratedTypes(MyOutboxJsonContext.Default) " +
                "inside AddOutbox(options => { ... }).");
        }

        // IOutboxMessageTypeResolver - required for type alias resolution.
        if (_serviceProvider.GetService<IOutboxMessageTypeResolver>() is null)
        {
            errors.Add(
                "IOutboxMessageTypeResolver is not registered. " +
                "Call options.UseTypeResolver(...) or options.UseGeneratedTypes() " +
                "inside AddOutbox(options => { ... }).");
        }

        // IOutboxRepository - required for persistence.
        if (_serviceProvider.GetService<IOutboxRepository>() is null)
        {
            errors.Add(
                "IOutboxRepository is not registered. " +
                "Add a storage provider, e.g., services.AddOutboxPostgreSql(...) or services.AddOutboxSqlServer(...).");
        }

        if (errors.Count > 0)
        {
            var message =
                $"EricksonLopez.Outbox startup validation failed. {errors.Count} critical service(s) are not registered:{Environment.NewLine}" +
                string.Join(Environment.NewLine, errors.ConvertAll(e => $"  - {e}"));

            _logger.StartupValidationFailed(errors.Count, string.Join("; ", errors));

            throw new InvalidOperationException(message);
        }

        // OutboxDispatcherBackgroundService - optional but warn if missing.
        // We look for any IHostedService that is of type OutboxDispatcherBackgroundService.
        var hasDispatcher = false;
        var hostedServices = _serviceProvider.GetServices<IHostedService>();
        foreach (var svc in hostedServices)
        {
            if (svc is EricksonLopez.Outbox.Dispatcher.OutboxDispatcherBackgroundService)
            {
                hasDispatcher = true;
                break;
            }
        }

        if (!hasDispatcher)
        {
            _logger.ProducerOnlyMode();
        }
        else
        {
            _logger.StartupValidationPassed();
        }

        // IDeadLetterRepository - ensure third-party implementations handle transaction=null.
        // Zero-Reflection check: uses the IsFirstPartyImplementation DIM on IDeadLetterRepository
        // instead of GetType().Name string comparison. First-party repos override the DIM to return true;
        // third-party repos that don't override return false, triggering the advisory log.
        var deadLetterRepo = _serviceProvider.GetService<IDeadLetterRepository>();
        if (deadLetterRepo is not null && !deadLetterRepo.IsFirstPartyImplementation)
        {
            _logger.ThirdPartyDeadLetterRepositoryRegistered(deadLetterRepo.GetType().ToString());
        }
    }
}




