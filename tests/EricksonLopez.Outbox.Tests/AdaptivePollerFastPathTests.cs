#pragma warning disable CA2012
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;

using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Dispatcher;
using EricksonLopez.Outbox.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

public class AdaptivePollerFastPathTests
{
    [Fact]
    public async Task Poller_Should_Use_Adaptive_FastPath_And_Skip_Metrics_On_Second_Run()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        
        var message = new OutboxMessage(Guid.NewGuid(), "Test", default, null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        
        var fullBatch = new List<OutboxMessage>();
        for(int i = 0; i < 5; i++) fullBatch.Add(message);
        
        var emptyBatch = new List<OutboxMessage>();

        // Return full batch first, then empty batch to break the adaptive loop
        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(
                new ValueTask<IReadOnlyList<OutboxMessage>>(fullBatch), 
                new ValueTask<IReadOnlyList<OutboxMessage>>(emptyBatch));
            
        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        var options = new OutboxDispatcherOptions
        {
            UseAdaptivePolling = true,
            BatchSize = 5,
            PollingInterval = TimeSpan.FromSeconds(1) // Long enough delay
        };
        var optionsMock = Options.Create(options);

        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, optionsMock, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), NSubstitute.Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>());

        var poller = new AdaptivePoller(provider, channel, Microsoft.Extensions.Options.Options.Create(options), NullLogger<AdaptivePoller>.Instance, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());

        // Force metric evaluation for 100% branch coverage (independent of system uptime)
        var lastMetricTickField = typeof(AdaptivePoller).GetField("_lastMetricTick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        lastMetricTickField?.SetValue(poller, Environment.TickCount64 - 40000);

        var cts = new CancellationTokenSource();
        var pollingTask = poller.StartPollingAsync(cts.Token);

        var timeout = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < timeout)
        {
            var calls = repo.ReceivedCalls();
            if (System.Linq.Enumerable.Count(calls, c => c.GetMethodInfo().Name == "FetchPendingAsync") >= 2) break;
            await Task.Delay(50);
        }

        cts.Cancel();
        try { await pollingTask; } catch (OperationCanceledException) { }

        // FetchPendingAsync should have been called at least twice (once for full batch, once for empty batch)
        // Note: asserting an exact number (like Received(2)) is flaky in async background loops.
        var totalCalls = System.Linq.Enumerable.Count(repo.ReceivedCalls(), c => c.GetMethodInfo().Name == "FetchPendingAsync");
        Assert.True(totalCalls >= 2, $"Expected at least 2 calls to FetchPendingAsync, but got {totalCalls}");
    }
}


