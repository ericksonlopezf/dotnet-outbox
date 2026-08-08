using System;
using System.Buffers;
using BenchmarkDotNet.Attributes;
using EricksonLopez.Outbox.Serialization;

namespace EricksonLopez.Outbox.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(iterationCount: 100, warmupCount: 20)]
[HideColumns("Error", "StdDev", "Median", "RatioSD")]
public class A_SerializationBenchmarks
{
    private NativeAotJsonSerializer _serializer = null!;
    private VariablePayloadEvent _event = null!;
    private ArrayBufferWriter<byte> _buffer = null!;

    [Params(512, 10240, 102400)] // 512 B, 10 KB, 100 KB
    public int PayloadSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _serializer = new NativeAotJsonSerializer(BenchmarkJsonContext.Default);
        var data = new string('x', PayloadSize);
        _event = new VariablePayloadEvent(Guid.NewGuid(), data);
        _buffer = new ArrayBufferWriter<byte>(PayloadSize + 1024);
    }

    /// <summary>
    /// Baseline: Serialize() allocates a new byte[] on every call.
    /// </summary>
    [Benchmark(Baseline = true)]
    public ReadOnlyMemory<byte> EricksonLopezOutbox_Serialize_Allocating()
    {
        return _serializer.Serialize(_event);
    }

    /// <summary>
    /// Optimized path: Serialize() writes directly to a reusable <c>IBufferWriter&lt;byte&gt;</c>.
    /// Zero intermediate byte[] allocation.
    /// </summary>
    [Benchmark]
    public void EricksonLopezOutbox_Serialize_BufferWriter()
    {
        _buffer.Clear();
        _serializer.Serialize(_event, _buffer);
    }
}
