using System;
using BenchmarkDotNet.Attributes;
using EricksonLopez.Outbox.Serialization;

namespace EricksonLopez.Outbox.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(iterationCount: 200, warmupCount: 20)]
[HideColumns("Error", "StdDev", "Median", "RatioSD")]
public class E_TypeResolutionBenchmarks
{
    private InMemoryMessageTypeResolver _inMemory = null!;
    private static readonly Type _messageType = typeof(OrderCreatedEvent);

    [GlobalSetup]
    public void Setup()
    {
        _inMemory = new InMemoryMessageTypeResolver(
        [
            ("order.created.v1", typeof(OrderCreatedEvent)),
            ("order.confirmed.v1", typeof(OrderConfirmedEvent)),
        ]);
    }

    [Benchmark(Baseline = true)]
    public string? InMemory_GetAlias()
    {
        _inMemory.TryGetAlias(_messageType, out var alias);
        return alias;
    }

    [Benchmark]
    public string? InMemory_Resolve()
    {
        var type = _inMemory.Resolve("order.created.v1");
        return type?.Name;
    }
}
