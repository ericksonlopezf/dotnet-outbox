using System;
using System.Collections.Generic;
using System.Linq;

namespace EricksonLopez.Outbox.Testing;

/// <summary>
/// Provides a concrete implementation of <see cref="IOutboxAssertion{TMessage}"/>.
/// </summary>
/// <remarks>
/// Enables fluent and readable assertions for tests that leverage the <see cref="InMemoryOutboxStore"/>.
/// </remarks>
/// <typeparam name="TMessage">The specific type of the message being asserted.</typeparam>
internal sealed class OutboxAssertion<TMessage> : IOutboxAssertion<TMessage>
    where TMessage : notnull
{
    private readonly IEnumerable<TMessage> _published;
    private Func<TMessage, bool>? _predicate;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxAssertion{TMessage}"/> class.
    /// </summary>
    /// <param name="published">The collection of messages published during the test run.</param>
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

/// <summary>
/// Provides a concrete implementation of <see cref="IOutboxTester"/> backed by an <see cref="InMemoryOutboxStore"/>.
/// </summary>
public sealed class OutboxTesterImpl : IOutboxTester
{
    private readonly InMemoryOutboxStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxTesterImpl"/> class.
    /// </summary>
    /// <param name="store">The in-memory outbox store that collects published messages.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public OutboxTesterImpl(InMemoryOutboxStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc/>
    public IOutboxAssertion<TMessage> ShouldHavePublished<TMessage>()
        where TMessage : notnull
    {
        var published = _store.GetPublishedMessages<TMessage>();
        return new OutboxAssertion<TMessage>(published);
    }
}
