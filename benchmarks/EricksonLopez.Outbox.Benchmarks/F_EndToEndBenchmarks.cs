using System;
using System.Buffers;
using BenchmarkDotNet.Attributes;
using EricksonLopez.Outbox.Serialization;
using EricksonLopez.Outbox.Testing;

namespace EricksonLopez.Outbox.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(iterationCount: 15, warmupCount: 10)]
[HideColumns("Error", "StdDev", "Median", "RatioSD")]
public class F_EndToEndBenchmarks
{
    private InMemoryOutboxStoreRepository _repo = null!;
    private NativeAotJsonSerializer _serializer = null!;
    private OrderCreatedEvent _event = null!;
    private ArrayBufferWriter<byte> _buffer = null!;

    [GlobalSetup]
    public void Setup()
    {
        _repo = new InMemoryOutboxStoreRepository();
        _serializer = new NativeAotJsonSerializer(BenchmarkJsonContext.Default);
        _event = new OrderCreatedEvent(Guid.NewGuid(), 99.99m, DateTimeOffset.UtcNow);
        _buffer = new ArrayBufferWriter<byte>(1024);
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        _repo.Reset();
    }

    /// <summary>
    /// Synthetic End-to-End benchmark simulating:
    /// Serialize -> Enqueue -> Dequeue -> Deserialize -> Handle
    /// </summary>
    [Benchmark]
    public async System.Threading.Tasks.ValueTask EricksonLopezOutbox_Synthetic_E2E()
    {
        // 1. Serialize
        _buffer.Clear();
        _serializer.Serialize(_event, _buffer);

        // 2. Build envelope
        var msg = new OutboxMessage(
            Id: Guid.NewGuid(),
            MessageType: "order.created.v1",
            Payload: _buffer.WrittenMemory.ToArray(), // Simulate bytes hitting DB
            CorrelationId: null,
            CausationId: null,
            Headers: Array.Empty<byte>(),
            CreatedAt: DateTimeOffset.UtcNow,
            ProcessedAt: null,
            DeliverAt: null,
            Status: EricksonLopez.Outbox.OutboxMessageStatus.Pending,
            RetryCount: 0,
            Error: null);

        // 3. Store
        await _repo.InsertAsync(msg, null!);

        // 4. Fetch (Dispatcher simulating DB read)
        var fetchedList = await _repo.FetchPendingAsync(1, default);
        var fetchedMsg = fetchedList[0];

        // 5. Deserialize
        var deserialized = _serializer.Deserialize<OrderCreatedEvent>(fetchedMsg.Payload.Span);

        // 6. Handle (dummy)
        if (deserialized == null)
            throw new InvalidOperationException("Deserialization failed");
    }
}
