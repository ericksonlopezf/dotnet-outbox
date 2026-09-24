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

    /// <exception cref="System.ArgumentNullException"><paramref name="dbTransaction"/> is <see langword="null"/></exception>
    public DbTransactionContext(DbTransaction dbTransaction)
    {
        DbTransaction = dbTransaction ?? throw new System.ArgumentNullException(nameof(dbTransaction));
    }
}
