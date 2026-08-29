// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using EricksonLopez.Outbox.Serialization;
using EricksonLopez.Result;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Serialization;

public partial class NativeAotJsonSerializerTests
{
    [Fact]
    public void Constructor_Should_Throw_On_Null_Context()
    {
        Action act = () => _ = new NativeAotJsonSerializer(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("context");
    }

    [Fact]
    public void Serialize_And_Deserialize_Should_Work_For_Registered_Types()
    {
        var context = TestJsonContext.Default;
        var serializer = new NativeAotJsonSerializer(context);

        var msg = new TestDto { Value = "Hello" };
        var bytes = serializer.Serialize(msg);

        bytes.Length.Should().BeGreaterThan(0);

        var deserialized = serializer.Deserialize<TestDto>(bytes.Span);
        deserialized.Should().NotBeNull();
        deserialized.Value.Should().Be("Hello");
    }

    [Fact]
    public void Serialize_Should_Work_For_Registered_Types()
    {
        var context = TestJsonContext.Default;
        var serializer = new NativeAotJsonSerializer(context);

        var msg = new TestDto { Value = "HelloBuffer" };
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        
        serializer.Serialize(msg, buffer);

        buffer.WrittenCount.Should().BeGreaterThan(0);
        var deserialized = serializer.Deserialize<TestDto>(buffer.WrittenSpan);
        deserialized.Should().NotBeNull();
        deserialized.Value.Should().Be("HelloBuffer");
    }

    [Fact]
    public void Serialize_Multiple_Times_Should_Reuse_Pooled_Writers()
    {
        var context = TestJsonContext.Default;
        var serializer = new NativeAotJsonSerializer(context);

        for (int i = 0; i < 20; i++)
        {
            var msg = new TestDto { Value = $"Value_{i}" };
            var buffer = new System.Buffers.ArrayBufferWriter<byte>();
            serializer.Serialize(msg, buffer);
            var deserialized = serializer.Deserialize<TestDto>(buffer.WrittenSpan);
            deserialized.Value.Should().Be($"Value_{i}");
        }
    }

    [Fact]
    public void Serialize_Should_Throw_For_Unregistered_Types()
    {
        var context = TestJsonContext.Default;
        var serializer = new NativeAotJsonSerializer(context);

        Action act = () => serializer.Serialize(new UnregisteredDto());
        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"Type {nameof(UnregisteredDto)} is not registered in the JsonSerializerContext.");
    }

    [Fact]
    public void SerializeWithBuffer_Should_Throw_For_Unregistered_Types()
    {
        var context = TestJsonContext.Default;
        var serializer = new NativeAotJsonSerializer(context);

        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        Action act = () => serializer.Serialize(new UnregisteredDto(), buffer);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"Type {nameof(UnregisteredDto)} is not registered in the JsonSerializerContext.");
    }

    [Fact]
    public void Deserialize_Should_Throw_For_Unregistered_Types()
    {
        var context = TestJsonContext.Default;
        var serializer = new NativeAotJsonSerializer(context);

        Action act = () => serializer.Deserialize<UnregisteredDto>(ReadOnlySpan<byte>.Empty);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"Type {nameof(UnregisteredDto)} is not registered in the JsonSerializerContext.");
    }

    [Fact]
    public void Constructor_Should_Throw_On_Null_Pool()
    {
        Action act = () => _ = new NativeAotJsonSerializer(TestJsonContext.Default, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("writerPool");
    }

    [Theory]
    [InlineData(1, 4)]
    [InlineData(2, 4)]
    [InlineData(3, 6)]
    [InlineData(4, 8)]
    [InlineData(8, 16)]
    public void CreatePoolProvider_Should_Calculate_Correct_MaximumRetained(int processorCount, int expectedRetained)
    {
        var provider = NativeAotJsonSerializer.CreatePoolProvider(processorCount);
        provider.Should().NotBeNull();
        provider.MaximumRetained.Should().Be(expectedRetained);
    }

    [Fact]
    public void CreateDefaultPool_Should_Return_Initialized_Pool()
    {
        var pool = NativeAotJsonSerializer.CreateDefaultPool();
        pool.Should().NotBeNull();

        var writer = pool.Get();
        writer.Should().NotBeNull();
        pool.Return(writer);
    }

    [Fact]
    public void Utf8JsonWriterPooledObjectPolicy_Create_And_Return_Should_Work()
    {
        var policy = new NativeAotJsonSerializer.Utf8JsonWriterPooledObjectPolicy();
        var writer = policy.Create();
        writer.Should().NotBeNull();
        policy.Return(writer).Should().BeTrue();
    }

    [Fact]
    public void Serialize_WithBuffer_Should_Return_Writer_To_Pool_And_Reset_Buffer_Reference()
    {
        var policy = new NativeAotJsonSerializer.Utf8JsonWriterPooledObjectPolicy();
        Utf8JsonWriter? returnedWriter = null;
        int returnCount = 0;
        int getCount = 0;

        var mockPool = new TrackingObjectPool<Utf8JsonWriter>(
            onGet: () =>
            {
                getCount++;
                return policy.Create();
            },
            onReturn: w =>
            {
                returnCount++;
                returnedWriter = w;
            });

        var serializer = new NativeAotJsonSerializer(TestJsonContext.Default, mockPool);
        var trackingBuffer = new TrackingBufferWriter();

        var msg = new TestDto { Value = "TestReset" };
        serializer.Serialize(msg, trackingBuffer);

        getCount.Should().Be(1);
        returnCount.Should().Be(1);
        returnedWriter.Should().NotBeNull();

        // Lock buffer so any subsequent writes throw
        trackingBuffer.IsLocked = true;

        // Flushing or writing to the returned writer must NOT hit trackingBuffer because writer was reset to Stream.Null
        returnedWriter!.WriteNullValue();
        returnedWriter.Flush();
        trackingBuffer.CallsAfterLock.Should().Be(0);
    }

    private sealed class TrackingObjectPool<T> : Microsoft.Extensions.ObjectPool.ObjectPool<T> where T : class
    {
        private readonly Func<T> _onGet;
        private readonly Action<T> _onReturn;

        public TrackingObjectPool(Func<T> onGet, Action<T> onReturn)
        {
            _onGet = onGet;
            _onReturn = onReturn;
        }

        public override T Get() => _onGet();
        public override void Return(T obj) => _onReturn(obj);
    }

    private sealed class TrackingBufferWriter : System.Buffers.IBufferWriter<byte>
    {
        private readonly System.Buffers.ArrayBufferWriter<byte> _inner = new();
        public bool IsLocked { get; set; }
        public int CallsAfterLock { get; private set; }

        public void Advance(int count)
        {
            if (IsLocked) CallsAfterLock++;
            _inner.Advance(count);
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            if (IsLocked) CallsAfterLock++;
            return _inner.GetMemory(sizeHint);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            if (IsLocked) CallsAfterLock++;
            return _inner.GetSpan(sizeHint);
        }
    }

    [Fact]
    public void HeadersJsonContext_Default_Should_Serialize_And_Deserialize_Dictionary()
    {
        var dict = new Dictionary<string, string> { ["k1"] = "v1", ["k2"] = "v2" };
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(dict, HeadersJsonContext.Default.DictionaryStringString);
        jsonBytes.Length.Should().BeGreaterThan(0);

        var result = JsonSerializer.Deserialize(jsonBytes, HeadersJsonContext.Default.DictionaryStringString);
        result.Should().NotBeNull();
        result!["k1"].Should().Be("v1");
        result["k2"].Should().Be("v2");
    }

    public class TestDto { public string Value { get; set; } = ""; }
    public class UnregisteredDto { }

    [JsonSerializable(typeof(TestDto))]
    internal sealed partial class TestJsonContext : JsonSerializerContext
    {
    }
}

