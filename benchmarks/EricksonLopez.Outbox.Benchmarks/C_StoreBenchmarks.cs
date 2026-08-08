using System;
using BenchmarkDotNet.Attributes;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Testing;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace EricksonLopez.Outbox.Benchmarks;

/// <remarks>
/// NOTE: This benchmark spawns child processes via BenchmarkDotNet's default toolchain.
/// If you have Malwarebytes or Windows Defender with PUA protection, add an exclusion for
/// the benchmark output directory, or run with: --job Dry for a quick smoke-test.
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(iterationCount: 15, warmupCount: 10)]
[HideColumns("Error", "StdDev", "Median", "RatioSD")]
public class C_StoreBenchmarks
{
    private InMemoryOutboxStore _store = null!;
    private OrderCreatedEvent _event = null!;
    private IServiceProvider _serviceProvider = null!;
    private Microsoft.Extensions.Hosting.IHost _host = null!;
    private IPublishEndpoint _massTransitPublishEndpoint = null!;
    private IMessageBus _wolverineBus = null!;

    [GlobalSetup]
    public void Setup()
    {
        _store = new InMemoryOutboxStore();
        _event = new OrderCreatedEvent(Guid.NewGuid(), 99.99m, DateTimeOffset.UtcNow);

        var hostBuilder = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // MassTransit
                services.AddMassTransit(x =>
                {
                    x.UsingInMemory((busContext, cfg) =>
                    {
                        cfg.ConfigureEndpoints(busContext);
                    });
                });

                // Wolverine
                services.AddWolverine(opts => { });
            });

        _host = hostBuilder.Build();
        _host.StartAsync().GetAwaiter().GetResult();

        _serviceProvider = _host.Services;
        _massTransitPublishEndpoint = _serviceProvider.GetRequiredService<IPublishEndpoint>();
        _wolverineBus = _serviceProvider.GetRequiredService<IMessageBus>();
    }

    [GlobalCleanup]
    public async System.Threading.Tasks.Task Cleanup()
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        // OutboxStore cleanup
        var repo = _serviceProvider.GetRequiredService<EricksonLopez.Outbox.Persistence.IOutboxRepository>();
        if (repo is InMemoryOutboxStoreRepository inMemoryRepo)
        {
            inMemoryRepo.Reset();
        }
    }

    [Benchmark(Baseline = true)]
    public async System.Threading.Tasks.ValueTask MassTransit_InMemory_Publish()
    {
        await _massTransitPublishEndpoint.Publish(_event);
    }

    [Benchmark]
    public async System.Threading.Tasks.ValueTask Wolverine_InMemory_Publish()
    {
        await _wolverineBus.PublishAsync(_event);
    }

    [Benchmark]
    public async System.Threading.Tasks.ValueTask EricksonLopezOutbox_StoreAsync_Single()
    {
        await _store.StoreAsync(_event, null!);
    }

    [Benchmark]
    public async System.Threading.Tasks.ValueTask EricksonLopezOutbox_StoreAsync_Fluent()
    {
        await _store
            .Publish(_event)
            .WithTransaction(null!)
            .WithHeader("TenantId", "t-benchmark")
            .StoreAsync();
    }
}
