namespace EricksonLopez.Outbox.Persistence;

/// <summary>
/// A generic transaction context for the Outbox, replacing the hard dependency on ADO.NET's DbTransaction.
/// This enables future support for NoSQL databases and non-relational event stores.
/// </summary>
/// <remarks>
/// <para>
/// <b>Escape hatch for non-ADO.NET transaction contexts:</b><br/>
/// The <see cref="GetContext{T}"/> default interface method allows repository implementations
/// to retrieve the underlying transaction object without casting through a known interface.
/// Use this when integrating with ORMs or frameworks that do not expose a <c>DbTransaction</c>:
/// <code>
/// public sealed class MartenOutboxRepository : IOutboxRepository
/// {
///     public ValueTask InsertAsync(OutboxMessage record, IOutboxTransactionContext transaction, ...)
///     {
///         var session = transaction.GetContext&lt;IDocumentSession&gt;()
///             ?? throw new InvalidOperationException("Expected a Marten IDocumentSession.");
///         session.Store(record);
///         return ValueTask.CompletedTask;
///     }
/// }
/// </code>
/// </para>
/// <para>
/// <b>Interface evolution policy (v1 → v2):</b><br/>
/// This interface uses Default Interface Methods (DIM) for optional members, allowing the library
/// to add new optional capabilities in future versions without breaking existing implementations.
/// Members added in a future version will always have a default implementation.
/// Implementations that <b>do not</b> override the default will retain correct baseline behavior.
/// </para>
/// </remarks>
public interface IOutboxTransactionContext
{
    /// <summary>Gets the underlying transaction object as an untyped reference.</summary>
    object Transaction { get; }
    
    /// <summary>Gets the underlying connection object, or <see langword="null"/> if no connection is associated.</summary>
    object? Connection { get; }

    /// <summary>Returns the underlying transaction cast to <typeparamref name="T"/>, or <see langword="null"/> if the cast fails.</summary>
    /// <typeparam name="T">The target transaction type to cast to.</typeparam>
    /// <returns>The transaction cast to <typeparamref name="T"/>, or <see langword="null"/> if the cast is not valid.</returns>
    T? GetContext<T>() where T : class => Transaction as T;
}


/// <summary>
/// A specialized transaction context for relational ADO.NET databases.
/// </summary>
public interface IRelationalOutboxTransactionContext : IOutboxTransactionContext
{
    /// <summary>Gets the strongly typed <see cref="System.Data.Common.DbConnection"/> associated with this context, or <see langword="null"/> if none is available.</summary>
    System.Data.Common.DbConnection? DbConnection { get; }

    /// <summary>Gets the strongly typed <see cref="System.Data.Common.DbTransaction"/> associated with this context, or <see langword="null"/> if none is active.</summary>
    System.Data.Common.DbTransaction? DbTransaction { get; }
}

/// <summary>
/// An ADO.NET specific implementation of <see cref="IRelationalOutboxTransactionContext"/>.
/// </summary>
public sealed class DbTransactionContext : IRelationalOutboxTransactionContext
{
    /// <inheritdoc/>
    public System.Data.Common.DbTransaction? DbTransaction { get; }
    
    /// <inheritdoc/>
    public System.Data.Common.DbConnection? DbConnection => DbTransaction?.Connection;
    
    /// <inheritdoc/>
    public object Transaction => DbTransaction!;
    
    /// <inheritdoc/>
    public object? Connection => DbConnection;

    /// <summary>
    /// Initializes a new instance of the <see cref="DbTransactionContext"/> class.
    /// </summary>
    /// <param name="dbTransaction">The ADO.NET database transaction.</param>
    public DbTransactionContext(System.Data.Common.DbTransaction dbTransaction)
    {
        DbTransaction = dbTransaction;
    }
}

/// <summary>
/// Convenience extension methods for creating <see cref="IOutboxTransactionContext"/> instances.
/// </summary>
/// <remarks>
/// Eliminates the ceremony of <c>new DbTransactionContext(tx)</c> by providing
/// <c>tx.ToOutboxContext()</c> as a discoverable, fluent alternative.
/// </remarks>
public static class OutboxTransactionContextExtensions
{
    /// <summary>
    /// Wraps the specified <see cref="System.Data.Common.DbTransaction"/> in an <see cref="IOutboxTransactionContext"/>.
    /// </summary>
    /// <param name="transaction">The ADO.NET database transaction.</param>
    /// <returns>An <see cref="IOutboxTransactionContext"/> wrapping the transaction.</returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="transaction"/> is <see langword="null"/>.</exception>
    public static IOutboxTransactionContext ToOutboxContext(this System.Data.Common.DbTransaction transaction)
    {
        System.ArgumentNullException.ThrowIfNull(transaction);
        return new DbTransactionContext(transaction);
    }
}
