using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Dispatcher;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Pipeline;
using NSubstitute;
using Xunit;


namespace EricksonLopez.Outbox.Tests;

public class OutboxDispatcherBackgroundServiceTests
{
    [Fact]
    public async Task ExecuteAsync_Should_Start_Poller_And_Consumers()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        var options = new OutboxDispatcherOptions
        {
            MaxDegreeOfParallelism = 2,
            BatchSize = 10,
            UseAdaptivePolling = false
        };

        var publisher = Substitute.For<IBrokerPublisher>();
        var optionsMock = Options.Create(options);
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, optionsMock, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), NSubstitute.Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>());

        var poller = new AdaptivePoller(provider, channel, optionsMock, NullLogger<AdaptivePoller>.Instance, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());

        var service = new OutboxDispatcherBackgroundService(
            NullLogger<OutboxDispatcherBackgroundService>.Instance,
            poller,
            channel,
            optionsMock);

        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
        await service.StartAsync(cts.Token);
        
        await Task.Delay(1000);

        try { await service.StopAsync(default); } catch { }

        await repo.Received().FetchPendingAsync(options.BatchSize, Arg.Any<CancellationToken>());
    }
}


