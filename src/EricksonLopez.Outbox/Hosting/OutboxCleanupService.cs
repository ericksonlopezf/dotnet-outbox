// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EricksonLopez.Outbox.Hosting;

/// <summary>
/// Background service that periodically purges dispatched outbox messages when soft-delete mode (<c>DeleteOnDispatch = false</c>) is active.
/// </summary>
public sealed class OutboxCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly OutboxCleanupOptions _options;
    private readonly ILogger<OutboxCleanupService> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxCleanupService"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to resolve scoped repository instances.</param>
    /// <param name="options">The cleanup options governing retention and interval.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="timeProvider">Optional time provider for testing and clock virtualization.</param>
    public OutboxCleanupService(
        IServiceProvider serviceProvider,
        IOptions<OutboxCleanupOptions> options,
        ILogger<OutboxCleanupService> logger,
        TimeProvider? timeProvider = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Executes a single purge pass. Can be called directly for on-demand cleanup or testing.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of purged messages, or 0 if no repository is registered.</returns>
    public async Task<int> PerformCleanupAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetService<IOutboxRepository>();

        if (repository is not null)
        {
            var cutoff = _timeProvider.GetUtcNow() - _options.RetentionPeriod;
            var purgedCount = await repository.PurgeDispatchedMessagesAsync(cutoff, _options.BatchSize, cancellationToken);
            if (purgedCount > 0)
            {
                _logger.LogInformation("Purged {PurgedCount} dispatched outbox messages older than {Cutoff}.", purgedCount, cutoff);
            }
            return purgedCount;
        }

        return 0;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Outbox Cleanup Service is disabled.");
            return;
        }

        _logger.LogInformation(
            "Outbox Cleanup Service started. Retention: {RetentionPeriod}. Interval: {CleanupInterval}.",
            _options.RetentionPeriod,
            _options.CleanupInterval);

        // Stryker disable all : Loop termination check per ADR-013
        while (!stoppingToken.IsCancellationRequested)
        // Stryker restore all
        {
            try
            {
                await Task.Delay(_options.CleanupInterval, _timeProvider, stoppingToken);
                await PerformCleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Stryker disable all : Cancellation exit per ADR-013
                break;
                // Stryker restore all
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while executing Outbox Cleanup pass.");
            }
        }
    }
}



