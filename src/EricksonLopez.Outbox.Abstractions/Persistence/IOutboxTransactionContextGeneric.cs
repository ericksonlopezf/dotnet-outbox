// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Outbox.Persistence;

/// <summary>
/// Defines a strongly typed transaction context abstraction supporting relational and non-relational database transaction primitives.
/// </summary>
/// <typeparam name="TConnection">The underlying connection or client type.</typeparam>
/// <typeparam name="TTransaction">The underlying transaction or session type.</typeparam>
public interface IOutboxTransactionContext<out TConnection, out TTransaction> : IOutboxTransactionContext
{
    /// <summary>
    /// Gets the strongly typed underlying connection or database client.
    /// </summary>
    new TConnection? Connection { get; }

    /// <summary>
    /// Gets the strongly typed underlying active transaction or session.
    /// </summary>
    new TTransaction? Transaction { get; }
}
