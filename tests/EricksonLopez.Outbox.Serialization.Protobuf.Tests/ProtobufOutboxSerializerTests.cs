// Copyright © Erickson Lopez. MIT License.
using System;
using System.Buffers;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Hosting;
using EricksonLopez.Outbox.Serialization;
using EricksonLopez.Outbox.Serialization.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using ProtoBuf;
using Xunit;

namespace EricksonLopez.Outbox.Serialization.Protobuf.Tests;

public class ProtobufOutboxSerializerTests
{
    [ProtoContract]
    public class TestProtoMessage
    {
        [ProtoMember(1)]
        public int Id { get; set; }

        [ProtoMember(2)]
        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public void Serialize_NullMessage_ThrowsArgumentNullException()
    {
        var serializer = new ProtobufOutboxSerializer();
        var act = () => serializer.Serialize<TestProtoMessage>(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("message");
    }

    [Fact]
    public void Serialize_WithBufferWriter_NullArguments_ThrowsArgumentNullException()
    {
        var serializer = new ProtobufOutboxSerializer();
        var writer = new ArrayBufferWriter<byte>();
        var msg = new TestProtoMessage { Id = 1, Name = "Test" };

        var act1 = () => serializer.Serialize<TestProtoMessage>(null!, writer);
        act1.Should().Throw<ArgumentNullException>().WithParameterName("message");

        var act2 = () => serializer.Serialize(msg, null!);
        act2.Should().Throw<ArgumentNullException>().WithParameterName("buffer");
    }

    [Fact]
    public void Deserialize_EmptyData_ThrowsArgumentException()
    {
        var serializer = new ProtobufOutboxSerializer();
        var act = () => serializer.Deserialize<TestProtoMessage>(ReadOnlySpan<byte>.Empty);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Data to deserialize cannot be empty*")
            .WithParameterName("data");
    }

    [Fact]
    public void Serialize_And_Deserialize_RoundTripSucceeds()
    {
        var serializer = new ProtobufOutboxSerializer();
        var original = new TestProtoMessage { Id = 42, Name = "Protocol Buffers" };

        var bytes = serializer.Serialize(original);
        bytes.Length.Should().BeGreaterThan(0);

        var deserialized = serializer.Deserialize<TestProtoMessage>(bytes.Span);

        deserialized.Should().NotBeNull();
        deserialized.Id.Should().Be(42);
        deserialized.Name.Should().Be("Protocol Buffers");
    }

    [Fact]
    public void Serialize_WithBufferWriter_Succeeds()
    {
        var serializer = new ProtobufOutboxSerializer();
        var original = new TestProtoMessage { Id = 99, Name = "BufferWriter" };

        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(original, writer);

        var array = writer.WrittenMemory.ToArray();
        array.Length.Should().BeGreaterThan(0);

        var deserialized = serializer.Deserialize<TestProtoMessage>(array);
        deserialized.Id.Should().Be(99);
        deserialized.Name.Should().Be("BufferWriter");
    }

    [Fact]
    public void UseProtobufSerializer_NullOptions_ThrowsArgumentNullException()
    {
        Action act = () => ProtobufOutboxSerializationExtensions.UseProtobufSerializer(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void UseProtobufSerializer_RegistersSerializerInOptions()
    {
        var services = new ServiceCollection();
        services.AddOutbox(opt => opt.UseProtobufSerializer());

        var sp = services.BuildServiceProvider();
        var serializer = sp.GetService<IOutboxSerializer>();

        serializer.Should().NotBeNull();
        serializer.Should().BeOfType<ProtobufOutboxSerializer>();
    }
}
