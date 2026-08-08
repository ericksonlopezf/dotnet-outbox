using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

using EricksonLopez.Outbox.Persistence;
using Microsoft.Extensions.Options;

namespace EricksonLopez.Outbox.Idempotency;

/// <summary>
/// Orchestrates the periodic cleanup of expired idempotency records in the background.
/// </summary>
/// <remarks>
/// This background service prevents the inbox idempotency table from growing indefinitely
/// by routinely purging records older than the configured duplicate detection window.
/// </remarks>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed class InboxCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly OutboxInboxOptions _options;
    private readonly ILogger<InboxCleanupService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="InboxCleanupService"/> class.
    /// </summary>
    /// <param name="serviceProvider">The dependency injection container that resolves repository instances.</param>
    /// <param name="options">The configured inbox options governing retention periods.</param>
    /// <param name="logger">The logger that records cleanup activity and errors.</param>
    public InboxCleanupService(
        IServiceProvider serviceProvider,
        IOptions<OutboxInboxOptions> options,
        ILogger<InboxCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Inbox Cleanup Service started. Retention window: {RetentionPeriod}. Cleanup interval: 1 hour.",
            _options.RetentionPeriod);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.CleanupInterval, stoppingToken);

                await using var scope = _serviceProvider.CreateAsyncScope();

                var repo = scope.ServiceProvider
                    .GetService<IIdempotencyRepository>();

                if (repo is not null)
                {
                    var cutoff = DateTimeOffset.UtcNow - _options.DuplicateDetectionWindow;
                    await repo.PurgeExpiredRecordsAsync(cutoff, stoppingToken);
                    _logger.LogDebug("Purged idempotency records older than {Cutoff}.", cutoff);
                }
                else
                {
                    _logger.LogDebug("No IIdempotencyRepository registered; skipping cleanup.");
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during inbox cleanup.");
            }
        }
    }
}
