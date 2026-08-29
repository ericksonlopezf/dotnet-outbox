// Copyright © Erickson Lopez. MIT License.
using System;
using BenchmarkDotNet.Attributes;
using EricksonLopez.Outbox;

namespace EricksonLopez.Outbox.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(iterationCount: 200, warmupCount: 20)]
[HideColumns("Error", "StdDev", "Median", "RatioSD")]
public class B_MessageConstructionBenchmarks
{
    private static readonly ReadOnlyMemory<byte> _payload =
        System.Text.Encoding.UTF8.GetBytes("{\"OrderId\":\"abc\",\"Total\":99.99}");

    [Benchmark(Baseline = true)]
    public OutboxMessage EricksonLopezOutbox_CreateOutboxMessage()
    {
        return new OutboxMessage(
            Id: Guid.NewGuid(),
            MessageType: "order.created.v1",
            Payload: _payload,
            CorrelationId: "corr-123",
            CausationId: null,
            Headers: System.Text.Encoding.UTF8.GetBytes("{}"),
            CreatedAt: DateTimeOffset.UtcNow,
            ProcessedAt: null,
            DeliverAt: null,
            Status: EricksonLopez.Outbox.OutboxMessageStatus.Pending,
            RetryCount: 0,
            Error: null);
    }

    [Benchmark]
    public OutboxMessageMetadata EricksonLopezOutbox_CreateMessageMetadata()
    {
        return new OutboxMessageMetadata(
            correlationId: "corr-123",
            causationId: null,
            messageType: "order.created.v1",
            entries: new[]
            {
                new MetadataEntry("TenantId", "t-100"),
                new MetadataEntry("Region", "us-east-1")
            });
    }
}


