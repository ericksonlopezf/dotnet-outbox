// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox.Diagnostics;
using EricksonLopez.Outbox.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
internal sealed class OutboxDispatcherBackgroundService : BackgroundService
{
    private readonly ILogger<OutboxDispatcherBackgroundService> _logger;
    private readonly AdaptivePoller _poller;
    private readonly OutboxChannel _channel;
    private readonly OutboxDispatcherOptions _options;
    private readonly TimeProvider _timeProvider;

    public OutboxDispatcherBackgroundService(
        ILogger<OutboxDispatcherBackgroundService> logger,
        AdaptivePoller poller,
        OutboxChannel channel,
        IOptions<OutboxDispatcherOptions> options,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _poller = poller;
        _channel = channel;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    // Volatile int: 0 = stopped, 1 = running. 
    // Using int instead of bool for consistent NativeAOT behavior on ARM.
    private volatile int _isRunning;
    private readonly TaskCompletionSource<bool> _startedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Gets a value indicating whether the dispatcher's execute loop is currently active.
    /// </summary>
    /// <remarks>
    /// This property is used by the outbox health check to report the current state of the dispatcher.
    /// </remarks>
    public bool IsRunning => _isRunning == 1;

    /// <summary>
    /// Waits asynchronously and deterministically until the background service has completed its initialization and entered its execution loop.
    /// </summary>
    internal async ValueTask<bool> WaitForRunningAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning) return true;
        return await _startedTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Interlocked.Exchange(ref _isRunning, 1);
        _startedTcs.TrySetResult(true);
        try
        {
            // Stryker disable all 
            _logger.DispatcherStarting(
                _options.MaxDegreeOfParallelism,
                _options.BatchSize,
                _options.UseAdaptivePolling);
            // Stryker restore all


            // Spawn N consumer tasks (parallel publish workers)
            int consumerCount = Math.Max(1, _options.MaxDegreeOfParallelism);
            var consumerTasks = new List<Task>(consumerCount);

            for (int i = 0; i < consumerCount; i++)
            {
                var consumerId = i;
                consumerTasks.Add(Task.Run(async () =>
                {
                    _logger.DispatcherConsumerStarted(consumerId);
                    // Stryker disable all : Loop termination check per ADR-013
                    while (!stoppingToken.IsCancellationRequested)
                    // Stryker restore all
                    {
                        try
                        {
                            await _channel.ProcessMessagesAsync(stoppingToken).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger.DispatcherConsumerCrashed(ex, consumerId);
                            // Stryker disable all : Dispatcher crash backoff delay per ADR-013
                            await Task.Delay(TimeSpan.FromMilliseconds(5000), _timeProvider, stoppingToken).ConfigureAwait(false);
                            // Stryker restore all
                        }
                    }
                    _logger.DispatcherConsumerStopped(consumerId);
                }, CancellationToken.None));
            }

            // Single poller task (feeds the shared channel)
            var pollTask = Task.Run(async () => await _poller.StartPollingAsync(stoppingToken), CancellationToken.None);

            // Wait for all tasks to complete (they stop when stoppingToken is cancelled)
            await Task.WhenAll([pollTask, .. consumerTasks]);

            // Stryker disable once all 
            _logger.DispatcherStopped();
        }
        finally
        {
            Interlocked.Exchange(ref _isRunning, 0);
        }
    }
}



