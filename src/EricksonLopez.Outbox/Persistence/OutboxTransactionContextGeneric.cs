// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Outbox.Persistence;

/// <summary>
/// Provides a strongly-typed implementation of <see cref="IOutboxTransactionContext{TConnection, TTransaction}"/> for relational and non-relational database transaction primitives.
/// </summary>
/// <typeparam name="TConnection">The underlying connection or client type.</typeparam>
/// <typeparam name="TTransaction">The underlying transaction or session type.</typeparam>
public sealed class OutboxTransactionContext<TConnection, TTransaction> : IOutboxTransactionContext<TConnection, TTransaction>
{
    /// <inheritdoc/>
    public TConnection? Connection { get; }

    /// <inheritdoc/>
    public TTransaction? Transaction { get; }

    /// <inheritdoc/>
    object? IOutboxTransactionContext.Connection => Connection;

    /// <inheritdoc/>
    object IOutboxTransactionContext.Transaction => Transaction!;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxTransactionContext{TConnection, TTransaction}"/> class.
    /// </summary>
    /// <param name="connection">The underlying connection or database client.</param>
    /// <param name="transaction">The underlying active transaction or session.</param>
    /// <exception cref="ArgumentNullException"><paramref name="transaction"/> is <see langword="null"/>.</exception>
    public OutboxTransactionContext(TConnection? connection, TTransaction transaction)
    {
        Connection = connection;
        Transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
    }
}
