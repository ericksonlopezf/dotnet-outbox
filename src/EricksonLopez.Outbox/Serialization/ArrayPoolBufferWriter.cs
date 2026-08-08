using System;
using System.Buffers;

namespace EricksonLopez.Outbox.Serialization;

/// <summary>
/// An <see cref="IBufferWriter{T}"/> implementation that uses <see cref="ArrayPool{T}"/>.
/// MUST be disposed to return the rented array to the pool.
/// </summary>
internal sealed class ArrayPoolBufferWriter<T> : IBufferWriter<T>, IDisposable
{
    private T[] _buffer;
    private int _index;
    private const int DefaultInitialBufferSize = 256;

    public ArrayPoolBufferWriter(int initialCapacity = DefaultInitialBufferSize)
    {
        if (initialCapacity <= 0)
            throw new ArgumentException("Initial capacity must be greater than zero.", nameof(initialCapacity));

        _buffer = ArrayPool<T>.Shared.Rent(initialCapacity);
        _index = 0;
    }

    public ReadOnlyMemory<T> WrittenMemory => _buffer.AsMemory(0, _index);
    public ReadOnlySpan<T> WrittenSpan => _buffer.AsSpan(0, _index);
    public int WrittenCount => _index;
    public int Capacity => _buffer.Length;

    public void Clear()
    {
        _index = 0;
    }

    public void Advance(int count)
    {
        if (count < 0)
            throw new ArgumentException("Count cannot be negative.", nameof(count));
        if (_index > _buffer.Length - count)
            throw new InvalidOperationException("Advanced past capacity.");

        _index += count;
    }

    public Memory<T> GetMemory(int sizeHint = 0)
    {
        CheckAndResizeBuffer(sizeHint);
        return _buffer.AsMemory(_index);
    }

    public Span<T> GetSpan(int sizeHint = 0)
    {
        CheckAndResizeBuffer(sizeHint);
        return _buffer.AsSpan(_index);
    }

    private void CheckAndResizeBuffer(int sizeHint)
    {
        if (sizeHint < 0)
            throw new ArgumentException("Size hint cannot be negative.", nameof(sizeHint));

        if (sizeHint == 0)
            sizeHint = 1;

        // Stryker disable all : Buffer resizing logic depends on JSON payload size which is hard to assert
        if (sizeHint > _buffer.Length - _index)
        {
            int newSize = CalculateNewSize(_buffer.Length, sizeHint);

            var newBuffer = ArrayPool<T>.Shared.Rent(newSize);
            _buffer.AsSpan(0, _index).CopyTo(newBuffer);
            ReturnArrayToPool(_buffer);
            _buffer = newBuffer;
        }
        // Stryker restore all
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public void Dispose()
    {
        if (_buffer != null)
        {
            ReturnArrayToPool(_buffer);
            _buffer = null!;
        }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static void ReturnArrayToPool(T[] array)
    {
        ArrayPool<T>.Shared.Return(array, clearArray: true);
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static int CalculateNewSize(int currentLength, int sizeHint)
    {
        int growBy = Math.Max(sizeHint, currentLength);
        int newSize = currentLength + growBy;
        
        if ((uint)newSize > int.MaxValue)
        {
            newSize = currentLength + sizeHint;
            if ((uint)newSize > int.MaxValue)
                throw new InvalidOperationException("Requested buffer size is too large.");
        }
        
        return newSize;
    }
}
