using System;
using System.Buffers;
using EricksonLopez.Outbox.Serialization;
using AwesomeAssertions;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Serialization;

public class ArrayPoolBufferWriterTests
{
    [Fact]
    public void Constructor_WithValidCapacity_SetsInitialState()
    {
        using var writer = new ArrayPoolBufferWriter<byte>(100);
        writer.WrittenCount.Should().Be(0);
        writer.Capacity.Should().BeGreaterThanOrEqualTo(100);
        writer.WrittenMemory.Length.Should().Be(0);
        writer.WrittenSpan.Length.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithInvalidCapacity_ThrowsArgumentException(int capacity)
    {
        Action act = () => { var _ = new ArrayPoolBufferWriter<byte>(capacity); };
        act.Should().Throw<ArgumentException>().WithParameterName("initialCapacity");
    }

    [Fact]
    public void Clear_ResetsWrittenCount()
    {
        using var writer = new ArrayPoolBufferWriter<byte>();
        var span = writer.GetSpan(10);
        writer.Advance(5);
        writer.WrittenCount.Should().Be(5);
        
        writer.Clear();
        
        writer.WrittenCount.Should().Be(0);
    }

    [Fact]
    public void Advance_WithValidCount_UpdatesWrittenCount()
    {
        using var writer = new ArrayPoolBufferWriter<byte>();
        writer.GetSpan(10); // Ensure buffer has capacity
        writer.Advance(5);
        
        writer.WrittenCount.Should().Be(5);
    }

    [Fact]
    public void Advance_WithNegativeCount_ThrowsArgumentException()
    {
        using var writer = new ArrayPoolBufferWriter<byte>();
        Action act = () => writer.Advance(-1);
        act.Should().Throw<ArgumentException>().WithParameterName("count");
    }

    [Fact]
    public void Advance_PastCapacity_ThrowsInvalidOperationException()
    {
        using var writer = new ArrayPoolBufferWriter<byte>(10);
        Action act = () => writer.Advance(writer.Capacity + 1);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GetMemory_WithZeroSizeHint_ReturnsAtLeastOneElement()
    {
        using var writer = new ArrayPoolBufferWriter<byte>();
        var mem = writer.GetMemory(0);
        mem.Length.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void GetSpan_WithZeroSizeHint_ReturnsAtLeastOneElement()
    {
        using var writer = new ArrayPoolBufferWriter<byte>();
        var span = writer.GetSpan(0);
        span.Length.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void GetMemory_WithNegativeSizeHint_ThrowsArgumentException()
    {
        using var writer = new ArrayPoolBufferWriter<byte>();
        Action act = () => writer.GetMemory(-1);
        act.Should().Throw<ArgumentException>().WithParameterName("sizeHint");
    }

    [Fact]
    public void GetSpan_WithNegativeSizeHint_ThrowsArgumentException()
    {
        using var writer = new ArrayPoolBufferWriter<byte>();
        Action act = () => writer.GetSpan(-1);
        act.Should().Throw<ArgumentException>().WithParameterName("sizeHint");
    }

    [Fact]
    public void CheckAndResizeBuffer_WhenSizeHintExceedsCurrentCapacity_ResizesBuffer()
    {
        using var writer = new ArrayPoolBufferWriter<byte>(10);
        int initialCapacity = writer.Capacity;
        
        var mem = writer.GetMemory(initialCapacity + 1);
        
        writer.Capacity.Should().BeGreaterThanOrEqualTo(initialCapacity + 1);
        mem.Length.Should().BeGreaterThanOrEqualTo(initialCapacity + 1);
    }

    [Fact]
    public void CheckAndResizeBuffer_MaintainsExistingData()
    {
        using var writer = new ArrayPoolBufferWriter<byte>(10);
        var span = writer.GetSpan(5);
        span[0] = 1;
        span[1] = 2;
        writer.Advance(5);
        
        // Force resize
        var newSpan = writer.GetSpan(writer.Capacity + 1);
        
        writer.WrittenSpan[0].Should().Be(1);
        writer.WrittenSpan[1].Should().Be(2);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var writer = new ArrayPoolBufferWriter<byte>();
        writer.Dispose();
        Action act = () => writer.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void Advance_WithZeroCount_DoesNotThrow()
    {
        using var writer = new ArrayPoolBufferWriter<byte>();
        Action act = () => writer.Advance(0);
        act.Should().NotThrow();
    }

    [Fact]
    public void Advance_ExactlyToCapacity_DoesNotThrow()
    {
        using var writer = new ArrayPoolBufferWriter<byte>(10);
        writer.GetSpan(10);
        Action act = () => writer.Advance(writer.Capacity);
        act.Should().NotThrow();
    }

    [Fact]
    public void GetMemory_WithExactCapacity_DoesNotResize()
    {
        using var writer = new ArrayPoolBufferWriter<byte>(10);
        int initialCapacity = writer.Capacity;
        var mem = writer.GetMemory(initialCapacity);
        writer.Capacity.Should().Be(initialCapacity);
    }
}
