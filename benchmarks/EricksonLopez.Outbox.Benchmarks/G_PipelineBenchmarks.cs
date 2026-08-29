// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Pipeline;

namespace EricksonLopez.Outbox.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(iterationCount: 100, warmupCount: 20)]
[HideColumns("Error", "StdDev", "Median", "RatioSD")]
public class G_PipelineBenchmarks
{
    private OutboxPipeline _pipeline0 = null!;
    private OutboxPipeline _pipeline1 = null!;
    private OutboxPipeline _pipeline3 = null!;
    private OutboxMessage _message = null!;

    [GlobalSetup]
    public void Setup()
    {
        _message = new OutboxMessage(
            Id: Guid.NewGuid(),
            MessageType: "order.created.v1",
            Payload: Array.Empty<byte>(),
            CorrelationId: null,
            CausationId: null,
            Headers: Array.Empty<byte>(),
            CreatedAt: DateTimeOffset.UtcNow,
            ProcessedAt: null,
            DeliverAt: null,
            Status: OutboxMessageStatus.Pending,
            RetryCount: 0,
            Error: null);

        OutboxPipelineDelegate terminal = (msg, meta, ct) => new ValueTask<DispatchResult>(DispatchResult.Ok());
        var middlewares = Array.Empty<IOutboxMiddleware>();
        _pipeline0 = new OutboxPipeline(middlewares, terminal);
        
        _pipeline1 = new OutboxPipeline([new DummyMiddleware()], terminal);
        
        _pipeline3 = new OutboxPipeline(
            [new DummyMiddleware(), new DummyMiddleware(), new DummyMiddleware()], 
            terminal);
    }

    [Benchmark(Baseline = true)]
    public async ValueTask ZeroMiddlewares()
    {
        await _pipeline0.ExecuteAsync(_message, default, CancellationToken.None);
    }

    [Benchmark]
    public async ValueTask OneMiddleware()
    {
        await _pipeline1.ExecuteAsync(_message, default, CancellationToken.None);
    }

    [Benchmark]
    public async ValueTask ThreeMiddlewares()
    {
        await _pipeline3.ExecuteAsync(_message, default, CancellationToken.None);
    }

    private sealed class DummyMiddleware : IOutboxMiddleware
    {
        public ValueTask<DispatchResult> InvokeAsync(OutboxMessage message, OutboxMessageMetadata metadata, OutboxPipelineDelegate next, CancellationToken cancellationToken)
        {
            return next(message, metadata, cancellationToken);
        }
    }
}




