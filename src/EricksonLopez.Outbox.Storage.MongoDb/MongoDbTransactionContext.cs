// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox.Persistence;
using MongoDB.Driver;

namespace EricksonLopez.Outbox.Storage.MongoDb;

/// <summary>
/// Provides a wrapper around a MongoDB <see cref="IClientSessionHandle"/> as an <see cref="IOutboxTransactionContext"/>.
/// </summary>
public sealed class MongoDbTransactionContext : IOutboxTransactionContext, IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Gets the underlying MongoDB client session.
    /// </summary>
    public IClientSessionHandle Session { get; }

    /// <inheritdoc/>
    public object Transaction => Session;

    /// <inheritdoc/>
    public object? Connection => Session.Client;

    /// <summary>
    /// Initializes a new instance of the <see cref="MongoDbTransactionContext"/> class.
    /// </summary>
    /// <param name="session">The MongoDB client session handle.</param>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is <see langword="null"/>.</exception>
    public MongoDbTransactionContext(IClientSessionHandle session)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
    }

    /// <summary>
    /// Commits the active MongoDB transaction asynchronously.
    /// </summary>
    public ValueTask CommitAsync(CancellationToken cancellationToken = default)
    {
        if (Session.IsInTransaction)
        {
            return new ValueTask(Session.CommitTransactionAsync(cancellationToken));
        }
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Aborts the active MongoDB transaction asynchronously.
    /// </summary>
    public ValueTask RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (Session.IsInTransaction)
        {
            return new ValueTask(Session.AbortTransactionAsync(cancellationToken));
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        Session.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Session.Dispose();
    }
}
