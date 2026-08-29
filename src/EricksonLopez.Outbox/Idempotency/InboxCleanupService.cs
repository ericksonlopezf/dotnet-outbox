// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EricksonLopez.Outbox.Idempotency;

/// <summary>
/// Orchestrates the periodic cleanup of expired idempotency records in the background.
/// </summary>
/// <remarks>
/// This background service prevents the inbox idempotency table from growing indefinitely
/// by routinely purging records older than the configured duplicate detection window.
/// </remarks>
public sealed class InboxCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly OutboxInboxOptions _options;
    private readonly ILogger<InboxCleanupService> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="InboxCleanupService"/> class.
    /// </summary>
    /// <param name="serviceProvider">The dependency injection container that resolves repository instances.</param>
    /// <param name="options">The configured inbox options governing retention periods.</param>
    /// <param name="logger">The logger that records cleanup activity and errors.</param>
    /// <param name="timeProvider">Optional time provider for testing and clock virtualization.</param>
    public InboxCleanupService(
        IServiceProvider serviceProvider,
        IOptions<OutboxInboxOptions> options,
        ILogger<InboxCleanupService> logger,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Executes a single purge pass for expired idempotency records. Can be called directly for on-demand cleanup or testing.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>true</c> if a repository was found and purge executed; otherwise, <c>false</c>.</returns>
    public async Task<bool> PerformCleanupAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();

        var repo = scope.ServiceProvider
            .GetService<IIdempotencyRepository>();

        if (repo is not null)
        {
            var cutoff = _timeProvider.GetUtcNow() - _options.DuplicateDetectionWindow;
            await repo.PurgeExpiredRecordsAsync(cutoff, cancellationToken);
            _logger.LogDebug("Purged idempotency records older than {Cutoff}.", cutoff);
            return true;
        }

        _logger.LogDebug("No IIdempotencyRepository registered; skipping cleanup.");
        return false;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Inbox Cleanup Service started. Retention window: {RetentionPeriod}. Cleanup interval: 1 hour.",
            _options.RetentionPeriod);

        // Stryker disable Boolean,Conditional,Equality : Loop termination check per ADR-013
        while (!stoppingToken.IsCancellationRequested)
        // Stryker restore Boolean,Conditional,Equality
        {
            try
            {
                await Task.Delay(_options.CleanupInterval, _timeProvider, stoppingToken);
                await PerformCleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Stryker disable Boolean,Conditional,Block,Statement : Cancellation exit per ADR-013
                break;
                // Stryker restore Boolean,Conditional,Block,Statement
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during inbox cleanup.");
            }
        }
    }
}



