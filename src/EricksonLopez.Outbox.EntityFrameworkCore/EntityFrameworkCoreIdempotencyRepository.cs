using System;
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
/// Entity Framework Core implementation of <see cref="IIdempotencyRepository"/>.
/// </summary>
/// <typeparamref name="TDbContext">The application's DbContext type.</typeparamref>
public class EntityFrameworkCoreIdempotencyRepository<TDbContext> : IIdempotencyRepository
    where TDbContext : DbContext
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityFrameworkCoreIdempotencyRepository{TDbContext}"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider to resolve scoped DbContext instances.</param>
    /// <exception cref="ArgumentNullException"><paramref name="serviceProvider"/> is <see langword="null"/>.</exception>
    public EntityFrameworkCoreIdempotencyRepository(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <inheritdoc/>
    public async ValueTask<bool> TryInsertAsync(
        IdempotencyRecord record,
        EricksonLopez.Outbox.Persistence.IOutboxTransactionContext? transaction = default,
        CancellationToken cancellationToken = default)
    {
        var dbContext = _serviceProvider.GetRequiredService<TDbContext>();

        var existing = await dbContext.Set<IdempotencyRecordEntity>()
            .FindAsync(new object[] { record.MessageId, record.ConsumerId }, cancellationToken)
            .ConfigureAwait(false);

        if (existing != null)
        {
            return false;
        }

        var entity = IdempotencyRecordEntity.FromModel(record);
        dbContext.Set<IdempotencyRecordEntity>().Add(entity);
        return true;
    }

    /// <inheritdoc/>
    public async ValueTask PurgeExpiredRecordsAsync(
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

        var oldRecords = await dbContext.Set<IdempotencyRecordEntity>()
            .Where(r => r.ProcessedAt < olderThan)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Stryker disable once all
        if (oldRecords.Count > 0)
        {
            dbContext.Set<IdempotencyRecordEntity>().RemoveRange(oldRecords);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}

