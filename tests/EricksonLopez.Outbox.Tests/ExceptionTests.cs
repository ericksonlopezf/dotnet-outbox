using System;
using AwesomeAssertions;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

public class ExceptionTests
{
    [Fact]
    public void OutboxHeadersTooLargeException_Constructors_WorkCorrectly()
    {
        var ex1 = new OutboxHeadersTooLargeException(100, 50);
        ex1.Message.Should().NotBeNullOrEmpty();
        ex1.ActualSize.Should().Be(100);
        ex1.MaxAllowedSize.Should().Be(50);
    }

    [Fact]
    public void OutboxPayloadTooLargeException_Constructors_WorkCorrectly()
    {
        var ex1 = new OutboxPayloadTooLargeException(100, 50);
        ex1.Message.Should().NotBeNullOrEmpty();
        ex1.ActualSize.Should().Be(100);
        ex1.MaxAllowedSize.Should().Be(50);
    }

    [Fact]
    public void OutboxRuntimeException_Constructors_WorkCorrectly()
    {
        var ex2 = new OutboxRuntimeException("test");
        ex2.Message.Should().Be("test");

        var inner = new InvalidOperationException("inner");
        var ex3 = new OutboxRuntimeException("test", inner);
        ex3.Message.Should().Be("test");
        ex3.InnerException.Should().Be(inner);
    }
}
