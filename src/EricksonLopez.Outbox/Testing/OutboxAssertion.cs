// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;

namespace EricksonLopez.Outbox.Testing;

/// <summary>
/// Provides a concrete implementation of <see cref="IOutboxAssertion{TMessage}"/>.
/// </summary>
internal sealed class OutboxAssertion<TMessage> : IOutboxAssertion<TMessage>
    where TMessage : notnull
{
    private readonly IEnumerable<TMessage> _published;
    private Func<TMessage, bool>? _predicate;

    internal OutboxAssertion(IEnumerable<TMessage> published)
    {
        _published = published;
    }

    /// <inheritdoc/>
    public IOutboxAssertion<TMessage> WithCondition(Func<TMessage, bool> predicate)
    {
        _predicate = predicate;
        return this;
    }

    /// <inheritdoc/>
    public void Once()
    {
        var count = CountMatching();
        if (count != 1)
            Fail(expectedCount: 1, actualCount: count);
    }

    /// <inheritdoc/>
    public void Times(int times)
    {
        var count = CountMatching();
        if (count != times)
            Fail(expectedCount: times, actualCount: count);
    }

    /// <inheritdoc/>
    public void AtLeastOnce()
    {
        if (CountMatching() == 0)
            throw new InvalidOperationException(
                $"Expected at least one {typeof(TMessage).Name} to be published, but none were found.");
    }

    /// <inheritdoc/>
    public void Never()
    {
        var count = CountMatching();
        if (count > 0)
            throw new InvalidOperationException(
                $"Expected {typeof(TMessage).Name} to never be published, but {count} were found.");
    }

    private int CountMatching()
    {
        var query = _published;
        return _predicate is null
            ? query.Count()
            : query.Count(_predicate);
    }

    private static void Fail(int expectedCount, int actualCount)
    {
        throw new InvalidOperationException(
            $"Expected {expectedCount} message(s) of type {typeof(TMessage).Name} to be published, but found {actualCount}.");
    }
}
