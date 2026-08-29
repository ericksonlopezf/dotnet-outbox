// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Inbox.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EricksonLopez.Inbox.Hosting;

/// <summary>
/// Provides a background service that periodically sweeps and purges expired inbox entries based on configured retention policies.
/// </summary>
public sealed class InboxCleanupBackgroundService : BackgroundService
{
    private readonly IInboxStore _inboxStore;
    private readonly IOptions<InboxOptions> _options;
    private readonly ILogger<InboxCleanupBackgroundService> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="InboxCleanupBackgroundService"/> class.
    /// </summary>
    /// <param name="inboxStore">The inbox persistence store.</param>
    /// <param name="options">The configured inbox options.</param>
    /// <param name="logger">Optional logger instance.</param>
    /// <param name="timeProvider">Optional time provider.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inboxStore"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    public InboxCleanupBackgroundService(
        IInboxStore inboxStore,
        IOptions<InboxOptions> options,
        ILogger<InboxCleanupBackgroundService>? logger = null,
        TimeProvider? timeProvider = null)
    {
        _inboxStore = inboxStore ?? throw new ArgumentNullException(nameof(inboxStore));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<InboxCleanupBackgroundService>.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _options.Value;
        if (!options.EnableAutomaticCleanup)
        {
            _logger.LogInformation("Automatic inbox cleanup is disabled.");
            return;
        }

        _logger.LogInformation(
            "Inbox cleanup background worker started. Interval={Interval}, RetentionPeriod={RetentionPeriod}",
            options.CleanupInterval,
            options.RetentionPeriod);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(options.CleanupInterval, _timeProvider, stoppingToken).ConfigureAwait(false);

                var threshold = _timeProvider.GetUtcNow().Subtract(options.RetentionPeriod);
                _logger.LogDebug("Purging inbox entries older than {Threshold}", threshold);

                await _inboxStore.PurgeExpiredEntriesAsync(threshold, stoppingToken).ConfigureAwait(false);

                _logger.LogDebug("Inbox sweep completed successfully.");
            }
            // Stryker disable Block, Statement : Equivalent loop break on cancellation
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            // Stryker restore Block, Statement
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during inbox cleanup execution.");
            }
        }
    }
}
