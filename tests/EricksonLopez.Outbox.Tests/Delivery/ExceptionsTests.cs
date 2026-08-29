// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Delivery;

public class ExceptionsTests
{
    [Fact]
    public void OutboxException_Constructors_ShouldSetProperties()
    {
        var ex1 = new OutboxException();
        ex1.Message.Should().NotBeNullOrWhiteSpace();

        var ex2 = new OutboxException("test message");
        ex2.Message.Should().Be("test message");

        var inner = new InvalidOperationException("inner");
        var ex3 = new OutboxException("test message 2", inner);
        ex3.Message.Should().Be("test message 2");
        ex3.InnerException.Should().Be(inner);
    }

    [Fact]
    public void OutboxTypeNotRegisteredException_Constructors_ShouldSetProperties()
    {
        var type = typeof(string);
        var ex1 = new OutboxTypeNotRegisteredException(type);
        ex1.MessageType.Should().Be(type);
        ex1.Message.Should().Be("Type System.String is not registered in the OutboxMessageTypeResolver. Decorate the type with [OutboxMessage(alias)] and register it during startup.");

        var inner = new InvalidOperationException("inner");
        var ex2 = new OutboxTypeNotRegisteredException(type, inner);
        ex2.MessageType.Should().Be(type);
        ex2.Message.Should().Be("Type System.String is not registered in the OutboxMessageTypeResolver.");
        ex2.InnerException.Should().Be(inner);
    }

    [Fact]
    public void OutboxSerializationException_Constructor_ShouldSetProperties()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new OutboxSerializationException("my_alias", inner);
        ex.MessageTypeAlias.Should().Be("my_alias");
        ex.Message.Should().Be("Failed to serialize message of type my_alias.");
        ex.InnerException.Should().Be(inner);
    }

    [Fact]
    public void OutboxConfigurationException_Constructor_ShouldSetProperties()
    {
        var ex = new OutboxConfigurationException("config error");
        ex.Message.Should().Be("config error");
    }

    [Fact]
    public void OutboxRuntimeException_Constructors_ShouldSetProperties()
    {
        var ex1 = new OutboxRuntimeException("runtime error");
        ex1.Message.Should().Be("runtime error");

        var inner = new InvalidOperationException("inner");
        var ex2 = new OutboxRuntimeException("runtime error", inner);
        ex2.Message.Should().Be("runtime error");
        ex2.InnerException.Should().Be(inner);
    }

    [Fact]
    public void OutboxDispatchException_Constructors_ShouldSetProperties()
    {
        var id = Guid.NewGuid();
        var inner = new InvalidOperationException("inner");
        var ex1 = new OutboxDispatchException(id, 3, "dispatch error", inner);
        ex1.MessageId.Should().Be(id);
        ex1.AttemptCount.Should().Be(3);
        ex1.Message.Should().Be("dispatch error");
        ex1.InnerException.Should().Be(inner);

        var ex2 = new OutboxDispatchException(id, 1, "dispatch error without inner");
        ex2.MessageId.Should().Be(id);
        ex2.AttemptCount.Should().Be(1);
        ex2.Message.Should().Be("dispatch error without inner");
    }

    [Fact]
    public void OutboxPayloadTooLargeException_Constructor_ShouldSetProperties()
    {
        var ex = new OutboxPayloadTooLargeException(1024, 512);
        ex.ActualSize.Should().Be(1024);
        ex.MaxAllowedSize.Should().Be(512);
        ex.Message.Should().Be("Payload size 1024 bytes exceeds the configured maximum of 512 bytes. Consider offloading the payload to blob storage and storing only a reference in the outbox message.");
    }

    [Fact]
    public void OutboxHeadersTooLargeException_Constructor_ShouldSetProperties()
    {
        var ex = new OutboxHeadersTooLargeException(2048, 1024);
        ex.ActualSize.Should().Be(2048);
        ex.MaxAllowedSize.Should().Be(1024);
        ex.Message.Should().Be("Headers size 2048 bytes exceeds the configured maximum of 1024 bytes. Reduce the number or size of message headers.");
    }
}
