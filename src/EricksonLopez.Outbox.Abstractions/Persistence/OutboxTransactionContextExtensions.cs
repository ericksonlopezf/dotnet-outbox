// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data.Common;

namespace EricksonLopez.Outbox.Persistence;

/// <summary>
/// Convenience extension methods for creating <see cref="IOutboxTransactionContext"/> instances.
/// </summary>
public static class OutboxTransactionContextExtensions
{
    /// <summary>
    /// Wraps the specified <see cref="DbTransaction"/> in an <see cref="IOutboxTransactionContext"/>.
    /// </summary>
    /// <param name="transaction">The ADO.NET database transaction.</param>
    /// <returns>An <see cref="IOutboxTransactionContext"/> wrapping the transaction.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="transaction"/> is <see langword="null"/>.</exception>
    public static IOutboxTransactionContext ToOutboxContext(this DbTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        return new DbTransactionContext(transaction);
    }
}
