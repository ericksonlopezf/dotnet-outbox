// Copyright © Erickson Lopez. MIT License.
using System;
using System.Buffers;
using AwesomeAssertions;
using EricksonLopez.Outbox.Serialization;
using EricksonLopez.Result;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Serialization;

public class ArrayPoolBufferWriterTests
{
    [Fact]
    public void Constructor_Default_SetsDefaultCapacity()
    {
        using var writer = new ArrayPoolBufferWriter<byte>();
        writer.WrittenCount.Should().Be(0);
        writer.Capacity.Should().BeGreaterThanOrEqualTo(256);
        writer.WrittenMemory.Length.Should().Be(0);
        writer.WrittenSpan.Length.Should().Be(0);
    }

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
    [InlineData(-100)]
    public void Constructor_WithInvalidCapacity_ThrowsArgumentException(int capacity)
    {
        Action act = () => { var _ = new ArrayPoolBufferWriter<byte>(capacity); };
        act.Should().Throw<ArgumentException>().WithParameterName("initialCapacity").WithMessage("Initial capacity must be greater than zero.*");
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
        writer.WrittenMemory.Length.Should().Be(0);
        writer.WrittenSpan.Length.Should().Be(0);
    }

    [Fact]
    public void Advance_WithValidCount_UpdatesWrittenCount()
    {
        using var writer = new ArrayPoolBufferWriter<byte>(100);
        writer.GetSpan(10);
        writer.Advance(5);
        
        writer.WrittenCount.Should().Be(5);
        writer.WrittenMemory.Length.Should().Be(5);
        writer.WrittenSpan.Length.Should().Be(5);

        writer.Advance(10);
        writer.WrittenCount.Should().Be(15);
    }

    [Fact]
    public void Advance_WithZeroCount_DoesNotThrow()
    {
        using var writer = new ArrayPoolBufferWriter<byte>();
        Action act = () => writer.Advance(0);
        act.Should().NotThrow();
        writer.WrittenCount.Should().Be(0);
    }

    [Fact]
    public void Advance_WithNegativeCount_ThrowsArgumentException()
    {
        using var writer = new ArrayPoolBufferWriter<byte>();
        Action act = () => writer.Advance(-1);
        act.Should().Throw<ArgumentException>().WithParameterName("count").WithMessage("Count cannot be negative.*");
    }

    [Fact]
    public void Advance_PastCapacity_ThrowsInvalidOperationException()
    {
        using var writer = new ArrayPoolBufferWriter<byte>(10);
        Action act = () => writer.Advance(writer.Capacity + 1);
        act.Should().Throw<InvalidOperationException>().WithMessage("Advanced past capacity.");
    }

    [Fact]
    public void Advance_ExactlyToCapacity_DoesNotThrow()
    {
        using var writer = new ArrayPoolBufferWriter<byte>(10);
        int cap = writer.Capacity;
        writer.GetSpan(cap);
        Action act = () => writer.Advance(cap);
        act.Should().NotThrow();
        writer.WrittenCount.Should().Be(cap);
    }

    [Fact]
    public void WrittenSpan_And_WrittenMemory_ExposeExactWrittenSegment()
    {
        using var writer = new ArrayPoolBufferWriter<byte>(100);
        var span = writer.GetSpan(3);
        span[0] = 42;
        span[1] = 43;
        span[2] = 44;
        writer.Advance(3);

        writer.WrittenCount.Should().Be(3);
        writer.WrittenSpan.Length.Should().Be(3);
        writer.WrittenSpan[0].Should().Be(42);
        writer.WrittenSpan[1].Should().Be(43);
        writer.WrittenSpan[2].Should().Be(44);

        writer.WrittenMemory.Length.Should().Be(3);
        writer.WrittenMemory.Span[0].Should().Be(42);
        writer.WrittenMemory.Span[1].Should().Be(43);
        writer.WrittenMemory.Span[2].Should().Be(44);
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
        act.Should().Throw<ArgumentException>().WithParameterName("sizeHint").WithMessage("Size hint cannot be negative.*");
    }

    [Fact]
    public void GetSpan_WithNegativeSizeHint_ThrowsArgumentException()
    {
        using var writer = new ArrayPoolBufferWriter<byte>();
        Action act = () => writer.GetSpan(-1);
        act.Should().Throw<ArgumentException>().WithParameterName("sizeHint").WithMessage("Size hint cannot be negative.*");
    }

    [Fact]
    public void GetMemory_WithExactCapacity_DoesNotResize()
    {
        using var writer = new ArrayPoolBufferWriter<byte>(10);
        int initialCapacity = writer.Capacity;
        var mem = writer.GetMemory(initialCapacity);
        writer.Capacity.Should().Be(initialCapacity);
    }

    [Fact]
    public void GetSpan_WithExactCapacity_DoesNotResize()
    {
        using var writer = new ArrayPoolBufferWriter<byte>(10);
        int initialCapacity = writer.Capacity;
        var span = writer.GetSpan(initialCapacity);
        writer.Capacity.Should().Be(initialCapacity);
    }

    [Fact]
    public void GetMemory_WithExactRemainingCapacity_DoesNotResize()
    {
        using var writer = new ArrayPoolBufferWriter<byte>(10);
        int initialCapacity = writer.Capacity;
        writer.Advance(5);
        var mem = writer.GetMemory(initialCapacity - 5);
        writer.Capacity.Should().Be(initialCapacity);
        mem.Length.Should().Be(initialCapacity - 5);
    }

    [Fact]
    public void GetSpan_WithExactRemainingCapacity_DoesNotResize()
    {
        using var writer = new ArrayPoolBufferWriter<byte>(10);
        int initialCapacity = writer.Capacity;
        writer.Advance(5);
        var span = writer.GetSpan(initialCapacity - 5);
        writer.Capacity.Should().Be(initialCapacity);
        span.Length.Should().Be(initialCapacity - 5);
    }

    [Fact]
    public void CheckAndResizeBuffer_WhenSizeHintExceedsCurrentCapacity_ResizesBuffer()
    {
        using var writer = new ArrayPoolBufferWriter<byte>(10);
        int initialCapacity = writer.Capacity;
        
        var mem = writer.GetMemory(initialCapacity + 50);
        
        writer.Capacity.Should().BeGreaterThanOrEqualTo(initialCapacity + 50);
        mem.Length.Should().BeGreaterThanOrEqualTo(initialCapacity + 50);
    }

    [Fact]
    public void CheckAndResizeBuffer_WhenSizeHintIsLargerThanDoubleCapacity_ResizesToSizeHint()
    {
        using var writer = new ArrayPoolBufferWriter<byte>(10);
        int initialCapacity = writer.Capacity;
        int largeHint = initialCapacity * 5;
        
        var span = writer.GetSpan(largeHint);
        
        writer.Capacity.Should().BeGreaterThanOrEqualTo(largeHint);
        span.Length.Should().BeGreaterThanOrEqualTo(largeHint);
    }

    [Fact]
    public void CheckAndResizeBuffer_MaintainsExistingData()
    {
        using var writer = new ArrayPoolBufferWriter<byte>(10);
        var span = writer.GetSpan(5);
        span[0] = 1;
        span[1] = 2;
        writer.Advance(2);
        
        // Force resize
        var newSpan = writer.GetSpan(writer.Capacity + 100);
        
        writer.WrittenSpan[0].Should().Be(1);
        writer.WrittenSpan[1].Should().Be(2);
        writer.WrittenCount.Should().Be(2);
    }

    [Fact]
    public void GetSpan_WhenBufferIsFullAndSizeHintZero_ResizesAndReturnsNonEmpty()
    {
        using var writer = new ArrayPoolBufferWriter<byte>(10);
        int cap = writer.Capacity;
        writer.Advance(cap);
        writer.WrittenCount.Should().Be(cap);
        
        var span = writer.GetSpan(0);
        span.Length.Should().BeGreaterThanOrEqualTo(1);
        writer.Capacity.Should().BeGreaterThan(cap);
    }

    [Fact]
    public void GetMemory_WhenBufferIsFullAndSizeHintZero_ResizesAndReturnsNonEmpty()
    {
        using var writer = new ArrayPoolBufferWriter<byte>(10);
        int cap = writer.Capacity;
        writer.Advance(cap);
        writer.WrittenCount.Should().Be(cap);
        
        var mem = writer.GetMemory(0);
        mem.Length.Should().BeGreaterThanOrEqualTo(1);
        writer.Capacity.Should().BeGreaterThan(cap);
    }

    [Fact]
    public void CalculateNewSize_WhenNormal_ReturnsDoubledOrHint()
    {
        ArrayPoolBufferWriter<byte>.CalculateNewSize(100, 10).Should().Be(200);
        ArrayPoolBufferWriter<byte>.CalculateNewSize(100, 50).Should().Be(200);
        ArrayPoolBufferWriter<byte>.CalculateNewSize(100, 100).Should().Be(200);
        ArrayPoolBufferWriter<byte>.CalculateNewSize(100, 250).Should().Be(350);
        ArrayPoolBufferWriter<byte>.CalculateNewSize(100, 300).Should().Be(400);
    }

    [Fact]
    public void CalculateNewSize_WhenResultIsExactlyIntMaxValue_ReturnsIntMaxValue()
    {
        // 1.0B + (int.MaxValue - 1.0B) = int.MaxValue (2,147,483,647)
        int result = ArrayPoolBufferWriter<byte>.CalculateNewSize(1_000_000_000, int.MaxValue - 1_000_000_000);
        result.Should().Be(int.MaxValue);
    }

    [Fact]
    public void CalculateNewSize_AtMaxDoublingCapacity_DoublesSuccessfully()
    {
        // MaxDoublingCapacity = 1_073_741_823. Doubling gives 2_147_483_646 (int.MaxValue - 1)
        int result = ArrayPoolBufferWriter<byte>.CalculateNewSize(ArrayPoolBufferWriter<byte>.MaxDoublingCapacity, 10);
        result.Should().Be(2_147_483_646);
    }

    [Fact]
    public void CalculateNewSize_JustAboveMaxDoublingCapacity_FallsBackToHint()
    {
        // MaxDoublingCapacity + 1 = 1_073_741_824. Doubling would overflow to 2_147_483_648, so falls back to hint
        int result = ArrayPoolBufferWriter<byte>.CalculateNewSize(ArrayPoolBufferWriter<byte>.MaxDoublingCapacity + 1, 10);
        result.Should().Be(1_073_741_824 + 10);
    }

    [Fact]
    public void CalculateNewSize_WhenDoublingOverflows_ReturnsCurrentPlusHint()
    {
        // 1.5B doubled is 3B (overflows uint > int.MaxValue), but 1.5B + 100 fits in int.MaxValue
        int result = ArrayPoolBufferWriter<byte>.CalculateNewSize(1_500_000_000, 100);
        result.Should().Be(1_500_000_100);
    }

    [Fact]
    public void CalculateNewSize_WhenDoublingOverflowsAndFallbackIsExactlyIntMaxValue_ReturnsIntMaxValue()
    {
        // 1.5B doubled is 3B (overflows), but 1.5B + (int.MaxValue - 1.5B) = int.MaxValue
        int result = ArrayPoolBufferWriter<byte>.CalculateNewSize(1_500_000_000, int.MaxValue - 1_500_000_000);
        result.Should().Be(int.MaxValue);
    }

    [Fact]
    public void CalculateNewSize_WhenBothOverflow_ThrowsInvalidOperationException()
    {
        Action act = () => ArrayPoolBufferWriter<byte>.CalculateNewSize(2_000_000_000, 1_000_000_000);
        act.Should().Throw<InvalidOperationException>().WithMessage("Requested buffer size is too large.");
    }

    [Fact]
    public void Dispose_SetsBufferToNull_AndIsIdempotent()
    {
        var writer = new ArrayPoolBufferWriter<byte>(100);
        writer.Dispose();

        // Buffer must be cleared (null) after Dispose
        Action getCapacity = () => _ = writer.Capacity;
        getCapacity.Should().Throw<NullReferenceException>();

        // Multiple calls to Dispose must not throw
        Action act = () => writer.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_Returns_And_Clears_Buffer_To_Pool()
    {
        var pool = ArrayPool<byte>.Shared;

        // Prime the size-256 thread-local pool bucket
        var primed = pool.Rent(256);
        pool.Return(primed, clearArray: true);

        var writer = new ArrayPoolBufferWriter<byte>(256);
        var span = writer.GetSpan(4);
        span[0] = 0xAA;
        span[1] = 0xBB;
        span[2] = 0xCC;
        span[3] = 0xDD;
        writer.Advance(4);

        writer.Dispose();

        // Rent from the same thread's pool bucket
        var rentedBack = pool.Rent(256);
        try
        {
            rentedBack.Should().BeSameAs(primed);
            rentedBack[0].Should().Be(0);
            rentedBack[1].Should().Be(0);
            rentedBack[2].Should().Be(0);
            rentedBack[3].Should().Be(0);
        }
        finally
        {
            pool.Return(rentedBack, clearArray: true);
        }
    }

    [Fact]
    public void Resize_Returns_And_Clears_Previous_Buffer_To_Pool()
    {
        var pool = ArrayPool<byte>.Shared;

        // Prime the size-256 thread-local pool bucket
        var primed = pool.Rent(256);
        pool.Return(primed, clearArray: true);

        var writer = new ArrayPoolBufferWriter<byte>(256);
        var span = writer.GetSpan(4);
        span[0] = 0xEE;
        span[1] = 0xFF;
        writer.Advance(2);

        // Force resize beyond current capacity
        writer.GetSpan(1000);

        // Old primed buffer MUST have been returned to the pool and cleared
        var rentedOld = pool.Rent(256);
        try
        {
            rentedOld.Should().BeSameAs(primed);
            rentedOld[0].Should().Be(0);
            rentedOld[1].Should().Be(0);
        }
        finally
        {
            pool.Return(rentedOld, clearArray: true);
            writer.Dispose();
        }
    }
}


