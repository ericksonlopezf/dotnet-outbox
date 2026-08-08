using System;
using AwesomeAssertions;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

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
        ex1.Message.Should().Contain(type.FullName!);

        var inner = new InvalidOperationException("inner");
        var ex2 = new OutboxTypeNotRegisteredException(type, inner);
        ex2.MessageType.Should().Be(type);
        ex2.Message.Should().Contain(type.FullName!);
        ex2.InnerException.Should().Be(inner);
    }

    [Fact]
    public void OutboxSerializationException_Constructor_ShouldSetProperties()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new OutboxSerializationException("my_alias", inner);
        ex.MessageTypeAlias.Should().Be("my_alias");
        ex.Message.Should().Contain("my_alias");
        ex.InnerException.Should().Be(inner);
    }

    [Fact]
    public void OutboxConfigurationException_Constructor_ShouldSetProperties()
    {
        var ex = new OutboxConfigurationException("config error");
        ex.Message.Should().Be("config error");
    }

    [Fact]
    public void OutboxDispatchException_Constructor_ShouldSetProperties()
    {
        var id = Guid.NewGuid();
        var inner = new InvalidOperationException("inner");
        var ex = new OutboxDispatchException(id, 3, "dispatch error", inner);
        
        ex.MessageId.Should().Be(id);
        ex.AttemptCount.Should().Be(3);
        ex.Message.Should().Be("dispatch error");
        ex.InnerException.Should().Be(inner);
    }
}


