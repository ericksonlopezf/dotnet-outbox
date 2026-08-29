// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Outbox.Persistence;

/// <summary>
/// Convenience extension methods for creating strongly-typed <see cref="IOutboxTransactionContext{TConnection, TTransaction}"/> instances.
/// </summary>
public static class OutboxGenericTransactionContextExtensions
{
    /// <summary>
    /// Wraps a transaction and optional connection in a strongly typed <see cref="IOutboxTransactionContext{TConnection, TTransaction}"/>.
    /// </summary>
    /// <typeparam name="TConnection">The type of the connection.</typeparam>
    /// <typeparam name="TTransaction">The type of the transaction.</typeparam>
    /// <param name="transaction">The active transaction or session instance.</param>
    /// <param name="connection">The optional connection or client instance.</param>
    /// <returns>An instance of <see cref="IOutboxTransactionContext{TConnection, TTransaction}"/>.</returns>
    public static IOutboxTransactionContext<TConnection, TTransaction> ToOutboxContext<TConnection, TTransaction>(
        this TTransaction transaction,
        TConnection? connection = default)
    {
        // Stryker disable once all 
        ArgumentNullException.ThrowIfNull(transaction);
        return new OutboxTransactionContext<TConnection, TTransaction>(connection, transaction);
    }
}
