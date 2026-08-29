// Copyright © Erickson Lopez. MIT License.
using System.Data.Common;

namespace EricksonLopez.Outbox.Persistence;

/// <summary>
/// Represents an ADO.NET database transaction implementation of <see cref="IRelationalOutboxTransactionContext"/>.
/// </summary>
public sealed class DbTransactionContext : IRelationalOutboxTransactionContext
{
    /// <inheritdoc/>
    public DbTransaction? DbTransaction { get; }

    /// <inheritdoc/>
    public DbConnection? DbConnection => DbTransaction?.Connection;

    /// <inheritdoc/>
    public object Transaction => DbTransaction!;

    /// <inheritdoc/>
    public object? Connection => DbConnection;

    /// <summary>
    /// Initializes a new instance of the <see cref="DbTransactionContext"/> class with the specified database transaction.
    /// </summary>
    /// <param name="dbTransaction">The ADO.NET database transaction.</param>
    public DbTransactionContext(DbTransaction dbTransaction)
    {
        DbTransaction = dbTransaction;
    }
}
