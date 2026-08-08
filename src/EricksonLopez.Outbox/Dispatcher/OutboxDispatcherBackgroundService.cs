using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using EricksonLopez.Outbox.Diagnostics;
using EricksonLopez.Outbox.Persistence;
using Microsoft.Extensions.Options;

namespace EricksonLopez.Outbox.Dispatcher;

/// <summary>
/// Orchestrates the Outbox Dispatcher lifecycle as a .NET BackgroundService.
/// </summary>
/// <remarks>
/// <para>
/// <b>Architecture:</b><br/>
///   1. A single <see cref="AdaptivePoller"/> feeds messages into the <see cref="OutboxChannel"/>.<br/>
///   2. N consumer tasks (N = <see cref="OutboxDispatcherOptions.MaxDegreeOfParallelism"/>)
///      drain the channel concurrently.
/// </para>
/// <para>
/// <b>The channel's BoundedCapacity provides natural backpressure:</b><br/>
///   - If consumers are slower than the poller, the channel fills up.<br/>
///   - The poller blocks on WriteAsync until a slot is available.<br/>
///   - This prevents memory runaway without dropping messages.
/// </para>
/// <para>
/// <b>Single poller + multiple consumers is the optimal pattern for PostgreSQL SKIP LOCKED:</b><br/>
///   - One process claims a batch from the database.<br/>
///   - Multiple parallel tasks publish to the broker concurrently.<br/>
///   - Broker throughput (not database throughput) is the bottleneck in practice.
/// </para>
/// </remarks>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
internal sealed class OutboxDispatcherBackgroundService : BackgroundService
{
    private readonly ILogger<OutboxDispatcherBackgroundService> _logger;
    private readonly AdaptivePoller _poller;
    private readonly OutboxChannel _channel;
    private readonly OutboxDispatcherOptions _options;

    public OutboxDispatcherBackgroundService(
        ILogger<OutboxDispatcherBackgroundService> logger,
        AdaptivePoller poller,
        OutboxChannel channel,
        IOptions<OutboxDispatcherOptions> options)
    {
        _logger = logger;
        _poller = poller;
        _channel = channel;
        _options = options.Value;
    }

    // Volatile int: 0 = stopped, 1 = running. 
    // Using int instead of bool for consistent NativeAOT behavior on ARM.
    private volatile int _isRunning;

    /// <summary>
    /// Gets a value indicating whether the dispatcher's execute loop is currently active.
    /// </summary>
    /// <remarks>
    /// This property is used by the outbox health check to report the current state of the dispatcher.
    /// </remarks>
    public bool IsRunning => _isRunning == 1;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Interlocked.Exchange(ref _isRunning, 1);
        try
        {
        _logger.DispatcherStarting(
            _options.MaxDegreeOfParallelism,
            _options.BatchSize,
            _options.UseAdaptivePolling);


        // Spawn N consumer tasks (parallel publish workers)
        int consumerCount = Math.Max(1, _options.MaxDegreeOfParallelism);
        var consumerTasks = new List<Task>(consumerCount);

        for (int i = 0; i < consumerCount; i++)
        {
            var consumerId = i;
            consumerTasks.Add(Task.Run(async () =>
            {
                _logger.DispatcherConsumerStarted(consumerId);
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await _channel.ProcessMessagesAsync(stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.DispatcherConsumerCrashed(ex, consumerId);
                        try
                        {
                            await Task.Delay(5000, stoppingToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }
                _logger.DispatcherConsumerStopped(consumerId);
            }, CancellationToken.None));
        }

        // Single poller task (feeds the shared channel)
        var pollTask = Task.Run(async () => await _poller.StartPollingAsync(stoppingToken), CancellationToken.None);

        // Wait for all tasks to complete (they stop when stoppingToken is cancelled)
        await Task.WhenAll([pollTask, .. consumerTasks]);

        _logger.DispatcherStopped();
        }
        finally
        {
            Interlocked.Exchange(ref _isRunning, 0);
        }
    }
}
