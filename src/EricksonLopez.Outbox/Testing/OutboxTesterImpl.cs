// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;

namespace EricksonLopez.Outbox.Testing;



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

