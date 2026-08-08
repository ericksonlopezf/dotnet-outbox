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
/// Entity Framework Core implementation of <see cref="IDeadLetterRepository"/>.
/// </summary>
/// <typeparamref name="TDbContext">The application's DbContext type.</typeparamref>
public class EntityFrameworkCoreDeadLetterRepository<TDbContext> : IDeadLetterRepository
    where TDbContext : DbContext
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityFrameworkCoreDeadLetterRepository{TDbContext}"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider to resolve scoped DbContext instances.</param>
    /// <exception cref="ArgumentNullException"><paramref name="serviceProvider"/> is <see langword="null"/>.</exception>
    public EntityFrameworkCoreDeadLetterRepository(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <inheritdoc/>
    public bool IsFirstPartyImplementation => true;

    /// <inheritdoc/>
    public async ValueTask InsertAsync(
        DeadLetterMessage message,
        EricksonLopez.Outbox.Persistence.IOutboxTransactionContext? transaction,
        CancellationToken cancellationToken = default)
    {
        var entity = DeadLetterMessageEntity.FromModel(message);
        var dbContext = _serviceProvider.GetRequiredService<TDbContext>();
        dbContext.Set<DeadLetterMessageEntity>().Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<DeadLetterMessage>> GetAsync(
        int limit = 100,
        DateTimeOffset? after = null,
        CancellationToken cancellationToken = default)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

        var query = dbContext.Set<DeadLetterMessageEntity>().AsQueryable();
        if (after.HasValue)
        {
            query = query.Where(d => d.DeadLetteredAt > after.Value);
        }

        var entities = await query
            .OrderBy(d => d.DeadLetteredAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities.Select(e => e.ToModel()).ToList();
    }

    /// <inheritdoc/>
    public async ValueTask DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

        var existing = await dbContext.Set<DeadLetterMessageEntity>()
            .FindAsync(new object[] { id }, cancellationToken)
            .ConfigureAwait(false);

        if (existing != null)
        {
            dbContext.Set<DeadLetterMessageEntity>().Remove(existing);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async ValueTask PurgeAsync(
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

        var oldMessages = await dbContext.Set<DeadLetterMessageEntity>()
            .Where(d => d.DeadLetteredAt < olderThan)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Stryker disable once all
        if (oldMessages.Count > 0)
        {
            dbContext.Set<DeadLetterMessageEntity>().RemoveRange(oldMessages);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}

