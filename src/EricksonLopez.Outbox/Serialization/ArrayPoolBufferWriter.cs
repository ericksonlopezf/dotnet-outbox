// Copyright © Erickson Lopez. MIT License.
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

        if (sizeHint > _buffer.Length - _index)
        {
            int newSize = CalculateNewSize(_buffer.Length, sizeHint);

            var newBuffer = ArrayPool<T>.Shared.Rent(newSize);
            _buffer.AsSpan(0, _index).CopyTo(newBuffer);
            ReturnArrayToPool(_buffer);
            _buffer = newBuffer;
        }
    }

    public void Dispose()
    {
        if (_buffer != null)
        {
            ReturnArrayToPool(_buffer);
            _buffer = null!;
        }
    }


    private static void ReturnArrayToPool(T[] array)
    {
        ArrayPool<T>.Shared.Return(array, clearArray: true);
    }

    internal const int MaxDoublingCapacity = int.MaxValue / 2; // 1,073,741,823

    internal static int CalculateNewSize(int currentLength, int sizeHint)
    {
        int growBy = Math.Max(sizeHint, currentLength);
        if (growBy == currentLength && currentLength > MaxDoublingCapacity)
        {
            growBy = sizeHint;
        }

        if (currentLength > int.MaxValue - growBy)
            throw new InvalidOperationException("Requested buffer size is too large.");

        return currentLength + growBy;
    }
}

