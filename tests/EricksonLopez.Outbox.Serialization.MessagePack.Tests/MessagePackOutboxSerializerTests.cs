// Copyright © Erickson Lopez. MIT License.
using System;
using System.Buffers;
using System.Linq;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Hosting;
using EricksonLopez.Outbox.Serialization;
using EricksonLopez.Outbox.Serialization.MessagePack;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EricksonLopez.Outbox.Serialization.MessagePack.Tests;

public class MessagePackOutboxSerializerTests
{
    [MessagePackObject]
    public class TestMessagePackMessage
    {
        [Key(0)]
        public int Id { get; set; }

        [Key(1)]
        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public void Constructor_CustomOptions_And_DefaultOptions_Work()
    {
        var customOptions = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4Block);
        var serializerCustom = new MessagePackOutboxSerializer(customOptions);

        var optionsField = typeof(MessagePackOutboxSerializer).GetField("_options", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var actualOptions = optionsField?.GetValue(serializerCustom);
        actualOptions.Should().BeSameAs(customOptions);

        var serializerDefault = new MessagePackOutboxSerializer();
        var defaultOptions = optionsField?.GetValue(serializerDefault);
        defaultOptions.Should().BeSameAs(MessagePackSerializerOptions.Standard);
    }

    [Fact]
    public void Serialize_NullMessage_ThrowsArgumentNullException()
    {
        var serializer = new MessagePackOutboxSerializer();
        var act = () => serializer.Serialize<TestMessagePackMessage>(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("message");
    }

    [Fact]
    public void Serialize_WithBufferWriter_NullArguments_ThrowsArgumentNullException()
    {
        var serializer = new MessagePackOutboxSerializer();
        var writer = new ArrayBufferWriter<byte>();
        var msg = new TestMessagePackMessage { Id = 1, Name = "Test" };

        var act1 = () => serializer.Serialize<TestMessagePackMessage>(null!, writer);
        act1.Should().Throw<ArgumentNullException>().WithParameterName("message");

        var act2 = () => serializer.Serialize(msg, null!);
        act2.Should().Throw<ArgumentNullException>().WithParameterName("buffer");
    }

    [Fact]
    public void Deserialize_EmptyData_ThrowsArgumentException()
    {
        var serializer = new MessagePackOutboxSerializer();
        var act = () => serializer.Deserialize<TestMessagePackMessage>(ReadOnlySpan<byte>.Empty);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Data to deserialize cannot be empty*")
            .WithParameterName("data");
    }

    [Fact]
    public void Serialize_And_Deserialize_RoundTripSucceeds()
    {
        var serializer = new MessagePackOutboxSerializer();
        var original = new TestMessagePackMessage { Id = 100, Name = "MessagePack Binary" };

        var bytes = serializer.Serialize(original);
        bytes.Length.Should().BeGreaterThan(0);

        var deserialized = serializer.Deserialize<TestMessagePackMessage>(bytes.Span);

        deserialized.Should().NotBeNull();
        deserialized.Id.Should().Be(100);
        deserialized.Name.Should().Be("MessagePack Binary");
    }

    [Fact]
    public void Serialize_WithBufferWriter_Succeeds()
    {
        var serializer = new MessagePackOutboxSerializer();
        var original = new TestMessagePackMessage { Id = 200, Name = "BufferWriter" };

        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(original, writer);

        var array = writer.WrittenMemory.ToArray();
        array.Length.Should().BeGreaterThan(0);

        var deserialized = serializer.Deserialize<TestMessagePackMessage>(array);
        deserialized.Id.Should().Be(200);
        deserialized.Name.Should().Be("BufferWriter");
    }

    [Fact]
    public void UseMessagePackSerializer_NullOptions_ThrowsArgumentNullException()
    {
        Action act = () => MessagePackOutboxSerializationExtensions.UseMessagePackSerializer(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void UseMessagePackSerializer_RegistersSerializerInOptions()
    {
        var services = new ServiceCollection();
        services.AddOutbox(opt => opt.UseMessagePackSerializer(MessagePackSerializerOptions.Standard));

        var sp = services.BuildServiceProvider();
        var serializer = sp.GetService<IOutboxSerializer>();

        serializer.Should().NotBeNull();
        serializer.Should().BeOfType<MessagePackOutboxSerializer>();
    }
}
