using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.EntityFrameworkCore.Entities;
using EricksonLopez.Outbox.Persistence;

namespace EricksonLopez.Outbox.EntityFrameworkCore;

/// <summary>
/// Entity Framework Core implementation of <see cref="IOutboxRepository"/>.
/// Operates on a <typeparamref name="TDbContext"/> to participate directly in EF Core transactions.
/// </summary>
/// <typeparam name="TDbContext">The application's <see cref="DbContext"/> type that owns the outbox tables.</typeparam>
public class EntityFrameworkCoreOutboxRepository<TDbContext> : IOutboxRepository
    where TDbContext : DbContext
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityFrameworkCoreOutboxRepository{TDbContext}"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider to resolve scoped DbContext instances.</param>
    /// <exception cref="ArgumentNullException"><paramref name="serviceProvider"/> is <see langword="null"/>.</exception>
    public EntityFrameworkCoreOutboxRepository(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <inheritdoc/>
    public ValueTask InsertAsync(
        OutboxMessage record,
        EricksonLopez.Outbox.Persistence.IOutboxTransactionContext transaction,
        CancellationToken cancellationToken = default)
    {
        var dbContext = _serviceProvider.GetRequiredService<TDbContext>();
        var entity = OutboxMessageEntity.FromModel(record);
        dbContext.Set<OutboxMessageEntity>().Add(entity);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask InsertBatchAsync(
        ReadOnlyMemory<OutboxMessage> records,
        EricksonLopez.Outbox.Persistence.IOutboxTransactionContext transaction,
        CancellationToken cancellationToken = default)
    {
        var dbContext = _serviceProvider.GetRequiredService<TDbContext>();
        var span = records.Span;
        var entities = new List<OutboxMessageEntity>(span.Length);
        for (int i = 0; i < span.Length; i++)
        {
            entities.Add(OutboxMessageEntity.FromModel(span[i]));
        }

        dbContext.Set<OutboxMessageEntity>().AddRange(entities);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<OutboxMessage>> FetchPendingAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

        var now = DateTimeOffset.UtcNow;
        var pending = await dbContext.Set<OutboxMessageEntity>()
            .Where(m => m.State == 0 && (m.DeliverAt == null || m.DeliverAt <= now))
            .OrderBy(m => m.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Stryker disable once all
        if (pending.Count == 0)
        {
            return Array.Empty<OutboxMessage>();
        }

        var claimedList = new List<OutboxMessage>(pending.Count);
        foreach (var msg in pending)
        {
            msg.State = 1;
            claimedList.Add(msg.ToModel());
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return claimedList;
    }

    /// <inheritdoc/>
    public async ValueTask MarkAsDispatchedAsync(
        IReadOnlyList<OutboxMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        // P2-FIX: Build list with foreach to avoid LINQ closure allocation.
        var idList = new List<Guid>();
        foreach (var m in messages) idList.Add(m.Id);
        // Stryker disable once all
        if (idList.Count == 0) return;

        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

        var now = DateTimeOffset.UtcNow;
        var entities = await dbContext.Set<OutboxMessageEntity>()
            .Where(m => idList.Contains(m.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var msg in entities)
        {
            msg.State = 2;
            msg.ProcessedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask MarkAsFailedAsync(
        IReadOnlyList<OutboxMessage> messages,
        string error,
        bool isDeadLetter = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        // P2-FIX: Build list with foreach to avoid LINQ closure allocation.
        var idList = new List<Guid>();
        foreach (var m in messages) idList.Add(m.Id);
        // Stryker disable once all
        if (idList.Count == 0) return;

        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

        var targetState = isDeadLetter ? 4 : 3;
        var entities = await dbContext.Set<OutboxMessageEntity>()
            .Where(m => idList.Contains(m.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var msg in entities)
        {
            msg.State = targetState;
            msg.Error = error;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<int> ReclaimStaleMessagesAsync(
        TimeSpan staleTimeout,
        CancellationToken cancellationToken = default)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

        var threshold = DateTimeOffset.UtcNow.Subtract(staleTimeout);
        var staleMessages = await dbContext.Set<OutboxMessageEntity>()
            .Where(m => m.State == 1 && m.CreatedAt < threshold)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);



        foreach (var msg in staleMessages)
        {
            msg.State = 0;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return staleMessages.Count;
    }

    /// <inheritdoc/>
    public async ValueTask<long> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
        return await dbContext.Set<OutboxMessageEntity>()
            .CountAsync(m => m.State == 0 || m.State == 3, cancellationToken)
            .ConfigureAwait(false);
    }
}

