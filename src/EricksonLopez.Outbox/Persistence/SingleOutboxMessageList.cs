// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections;
using System.Collections.Generic;

namespace EricksonLopez.Outbox.Persistence;

/// <summary>
/// A zero-allocation, stack-allocated <see cref="IEnumerable{T}"/> wrapper around a single <see cref="OutboxMessage"/>.
/// </summary>
internal readonly struct SingleOutboxMessageList : IReadOnlyList<OutboxMessage>
{
    private readonly OutboxMessage _message;

    public SingleOutboxMessageList(OutboxMessage message)
    {
        _message = message;
    }

    public int Count => 1;

    public OutboxMessage this[int index] => index == 0 ? _message : throw new ArgumentOutOfRangeException(nameof(index));

    /// <inheritdoc/>
    public Enumerator GetEnumerator() => new(_message);

    IEnumerator<OutboxMessage> IEnumerable<OutboxMessage>.GetEnumerator()
        => new Enumerator(_message);

    IEnumerator IEnumerable.GetEnumerator()
        => new Enumerator(_message);

    /// <summary>
    /// A struct-based enumerator for a single <see cref="OutboxMessage"/> — no heap allocation.
    /// </summary>
    public struct Enumerator : IEnumerator<OutboxMessage>
    {
        private readonly OutboxMessage _message;
        private int _state;

        public Enumerator(OutboxMessage message)
        {
            _message = message;
            _state = 0;
        }

        /// <inheritdoc/>
        public OutboxMessage Current => _message;
        object IEnumerator.Current => _message;

        /// <inheritdoc/>
        public bool MoveNext()
        {
            if (_state == 0)
            {
                _state = 1;
                return true;
            }
            _state = 2;
            return false;
        }

        /// <inheritdoc/>
        public void Reset() => _state = 0;

        /// <inheritdoc/>
        public void Dispose() { }
    }
}
