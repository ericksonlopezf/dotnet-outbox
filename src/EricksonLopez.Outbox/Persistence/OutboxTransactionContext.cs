using System;

namespace EricksonLopez.Outbox.Persistence;

/// <summary>
/// A generic implementation of <see cref="IOutboxTransactionContext"/> used for generic transactions.
/// </summary>
public sealed class OutboxTransactionContext : IOutboxTransactionContext
{
    /// <inheritdoc/>
    public object Connection { get; }
    
    /// <inheritdoc/>
    public object Transaction { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxTransactionContext"/> class.
    /// </summary>
    /// <param name="connection">The generic connection object.</param>
    /// <param name="transaction">The generic transaction object.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> or <paramref name="transaction"/> is <see langword="null"/>.</exception>
    public OutboxTransactionContext(object connection, object transaction)
    {
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        Transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
    }
}
