using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using EricksonLopez.Outbox.Serialization;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

public partial class NativeAotJsonSerializerTests
{
    [Fact]
    public void Constructor_Should_Throw_On_Null_Context()
    {
        Action act = () => _ = new NativeAotJsonSerializer(null!);
        act.Should().Throw<ArgumentNullException>();
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
    public void Serialize_Should_Throw_For_Unregistered_Types()
    {
        var context = TestJsonContext.Default;
        var serializer = new NativeAotJsonSerializer(context);

        Action act = () => serializer.Serialize(new UnregisteredDto());
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SerializeWithBuffer_Should_Throw_For_Unregistered_Types()
    {
        var context = TestJsonContext.Default;
        var serializer = new NativeAotJsonSerializer(context);

        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        Action act = () => serializer.Serialize(new UnregisteredDto(), buffer);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Deserialize_Should_Throw_For_Unregistered_Types()
    {
        var context = TestJsonContext.Default;
        var serializer = new NativeAotJsonSerializer(context);

        Action act = () => serializer.Deserialize<UnregisteredDto>(ReadOnlySpan<byte>.Empty);
        act.Should().Throw<InvalidOperationException>();
    }

    public class TestDto { public string Value { get; set; } = ""; }
    public class UnregisteredDto { }

    [JsonSerializable(typeof(TestDto))]
    internal sealed partial class TestJsonContext : JsonSerializerContext
    {
    }
}


