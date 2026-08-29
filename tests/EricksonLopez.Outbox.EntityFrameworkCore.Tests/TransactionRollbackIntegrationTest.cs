// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox.EntityFrameworkCore.Entities;
using EricksonLopez.Outbox.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EricksonLopez.Outbox.EntityFrameworkCore.Tests;

public class SqliteTestDbContext : DbContext
{
    public SqliteTestDbContext(DbContextOptions<SqliteTestDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyOutboxEntityConfigurations("outbox");
    }
}

public class TransactionRollbackIntegrationTest : IAsyncLifetime, IDisposable
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _serviceProvider = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContext<SqliteTestDbContext>(options => 
            options.UseSqlite(_connection));
            
        services.AddOutboxEntityFrameworkCore<SqliteTestDbContext>();
        _serviceProvider = services.BuildServiceProvider();

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SqliteTestDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        _serviceProvider?.Dispose();
        if (_connection != null)
        {
            await _connection.DisposeAsync();
        }
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
        _connection?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task InsertAsync_WhenTransactionRolledBack_DoesNotSaveToDatabase()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SqliteTestDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();

        // Act
        using (var transaction = await dbContext.Database.BeginTransactionAsync())
        {
            var msg = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
            await repo.InsertAsync(msg, null!, CancellationToken.None);
            await dbContext.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        // Assert
        var count = await dbContext.Set<OutboxMessageEntity>().CountAsync();
        count.Should().Be(0);
    }
    
    [Fact]
    public async Task InsertAsync_WhenTransactionCommitted_SavesToDatabase()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SqliteTestDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();

        // Act
        using (var transaction = await dbContext.Database.BeginTransactionAsync())
        {
            var msg = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
            await repo.InsertAsync(msg, null!, CancellationToken.None);
            await dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        // Assert
        var count = await dbContext.Set<OutboxMessageEntity>().CountAsync();
        count.Should().Be(1);
    }
}



