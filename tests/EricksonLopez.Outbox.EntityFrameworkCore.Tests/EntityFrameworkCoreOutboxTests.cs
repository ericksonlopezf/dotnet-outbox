using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using NSubstitute;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.EntityFrameworkCore.Entities;
using EricksonLopez.Outbox.Persistence;
using Xunit;

namespace EricksonLopez.Outbox.EntityFrameworkCore.Tests;

public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

    public DbSet<OutboxMessageEntity> Messages => Set<OutboxMessageEntity>();
    public DbSet<IdempotencyRecordEntity> IdempotencyRecords => Set<IdempotencyRecordEntity>();
    public DbSet<DeadLetterMessageEntity> DeadLetters => Set<DeadLetterMessageEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyOutboxEntityConfigurations("outbox");
    }
}

public class EntityFrameworkCoreOutboxTests
{
    private static TestDbContext CreateDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new TestDbContext(options);
    }

    private static ServiceProvider CreateServiceProvider(string dbName)
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(options => options.UseInMemoryDatabase(dbName));
        services.AddOutboxEntityFrameworkCore<TestDbContext>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task InsertAsync_Should_Attach_Message_To_DbContext_ChangeTracker()
    {
        var dbName = Guid.NewGuid().ToString();
        var sp = CreateServiceProvider(dbName);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        var msg = new OutboxMessage(
            Id: Guid.NewGuid(),
            MessageType: "OrderCreated",
            Payload: new byte[] { 1, 2, 3 },
            CorrelationId: "corr-1",
            CausationId: "cause-1",
            Headers: System.Text.Encoding.UTF8.GetBytes("{}"),
            CreatedAt: DateTimeOffset.UtcNow,
            ProcessedAt: null,
            DeliverAt: null,
            Status: 0,
            RetryCount: 0,
            Error: null);

        await repo.InsertAsync(msg, null!);
        await dbContext.SaveChangesAsync();

        var saved = await dbContext.Messages.FindAsync(msg.Id);
        saved.Should().NotBeNull();
        saved!.MessageType.Should().Be("OrderCreated");
        saved.State.Should().Be(0);


    }

    [Fact]
    public async Task FetchPendingAsync_Should_Claim_Pending_Messages()
    {
        var dbName = Guid.NewGuid().ToString();
        var sp = CreateServiceProvider(dbName);
        using (var scope = sp.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            dbContext.Messages.Add(new OutboxMessageEntity
            {
                Id = Guid.NewGuid(),
                MessageType = "EventA",
                Payload = new byte[] { 1 },
                CorrelationId = null,
                CausationId = null,
                HeadersJson = "{}",
                CreatedAt = DateTimeOffset.UtcNow,
                ProcessedAt = null,
                DeliverAt = null,
                State = 0,
                Error = null
            });
            await dbContext.SaveChangesAsync();
        }

        using (var scope = sp.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
            var fetched = await repo.FetchPendingAsync(10);

        fetched.Should().HaveCount(1);
        fetched[0].MessageType.Should().Be("EventA");
            fetched[0].Status.Should().Be((EricksonLopez.Outbox.OutboxMessageStatus)1); // Claimed as InFlight
        }
    }

    [Fact]
    public async Task FetchPendingAsync_Should_Return_Empty_When_No_Messages_Pending()
    {
        var dbName = Guid.NewGuid().ToString();
        var sp = CreateServiceProvider(dbName);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var fetched = await repo.FetchPendingAsync(10);
        fetched.Should().BeEmpty();
    }

    [Fact]
    public async Task MarkAsDispatchedAsync_Should_Update_Status_To_Dispatched()
    {
        var dbName = Guid.NewGuid().ToString();
        var sp = CreateServiceProvider(dbName);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var msgId = Guid.NewGuid();


        // Test empty
        await repo.MarkAsDispatchedAsync(Array.Empty<OutboxMessage>());

        using (var scope2 = sp.CreateScope())
        {
            var dbContext = scope2.ServiceProvider.GetRequiredService<TestDbContext>();
            dbContext.Messages.Add(new OutboxMessageEntity
            {
                Id = msgId,
                MessageType = "EventB",
                Payload = new byte[] { 1 },
                CorrelationId = null,
                CausationId = null,
                HeadersJson = "{}",
                CreatedAt = DateTimeOffset.UtcNow,
                ProcessedAt = null,
                DeliverAt = null,
                State = 1,
                Error = null
            });
            await dbContext.SaveChangesAsync();
        }

        await repo.MarkAsDispatchedAsync(new[] { new OutboxMessage(msgId, "", Array.Empty<byte>(), null, null, Array.Empty<byte>(), DateTimeOffset.UtcNow, null, null, 0, 0, null) });

        using (var scope3 = sp.CreateScope())
        {
            var dbContext = scope3.ServiceProvider.GetRequiredService<TestDbContext>();
            var updated = await dbContext.Messages.FindAsync(msgId);
            updated.Should().NotBeNull();
            updated!.State.Should().Be(2);
            updated.ProcessedAt.Should().NotBeNull();
        }
        
        Func<Task> act = async () => await repo.MarkAsDispatchedAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task TryInsertAsync_Idempotency_Should_Prevent_Duplicates()
    {
        var dbName = Guid.NewGuid().ToString();
        var sp = CreateServiceProvider(dbName);
        using var dbContext = CreateDbContext(dbName);
        var repo = new EntityFrameworkCoreIdempotencyRepository<TestDbContext>(sp);

        var record = new IdempotencyRecord("msg-1", "consumer-a", DateTimeOffset.UtcNow);

        var first = await repo.TryInsertAsync(record, null!);
        await dbContext.SaveChangesAsync();
        first.Should().BeTrue();

        var second = await repo.TryInsertAsync(record, null!);
        second.Should().BeFalse();


    }

    [Fact]
    public async Task InsertBatchAsync_Should_Add_Multiple_Messages()
    {
        var dbName = Guid.NewGuid().ToString();
        var sp = CreateServiceProvider(dbName);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        var msgs = new[]
        {
            new OutboxMessage(Guid.NewGuid(), "M1", Array.Empty<byte>(), null, null, Array.Empty<byte>(), DateTimeOffset.UtcNow, null, null, 0, 0, null),
            new OutboxMessage(Guid.NewGuid(), "M2", Array.Empty<byte>(), null, null, Array.Empty<byte>(), DateTimeOffset.UtcNow, null, null, 0, 0, null)
        };

        await repo.InsertBatchAsync(msgs, null!);
        await dbContext.SaveChangesAsync();

        var count = await dbContext.Messages.CountAsync();
        count.Should().Be(2);


    }

    [Fact]
    public async Task MarkAsFailedAsync_Should_Update_State_And_Error()
    {
        var dbName = Guid.NewGuid().ToString();
        var sp = CreateServiceProvider(dbName);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var msgId = Guid.NewGuid();


        // Test empty
        await repo.MarkAsFailedAsync(Array.Empty<OutboxMessage>(), "err");

            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            dbContext.Messages.Add(new OutboxMessageEntity { Id = msgId, MessageType = "A", Payload = Array.Empty<byte>(), HeadersJson = "{}", CreatedAt = DateTimeOffset.UtcNow, State = 1 });
            await dbContext.SaveChangesAsync();


        await repo.MarkAsFailedAsync(new[] { new OutboxMessage(msgId, "", Array.Empty<byte>(), null, null, Array.Empty<byte>(), DateTimeOffset.UtcNow, null, null, 0, 0, null) }, "err", isDeadLetter: false);

        using (var scope3 = sp.CreateScope())
        {
            var dbContext3 = scope3.ServiceProvider.GetRequiredService<TestDbContext>();
            var updated = await dbContext3.Messages.FindAsync(msgId);
            updated!.State.Should().Be(3);
            updated.Error.Should().Be("err");
        }

        await repo.MarkAsFailedAsync(new[] { new OutboxMessage(msgId, "", Array.Empty<byte>(), null, null, Array.Empty<byte>(), DateTimeOffset.UtcNow, null, null, 0, 0, null) }, "fatal", isDeadLetter: true);

        using (var scope2 = sp.CreateScope())
        {
            var dbContext2 = scope2.ServiceProvider.GetRequiredService<TestDbContext>();
            var updated2 = await dbContext2.Messages.FindAsync(msgId);
            updated2!.State.Should().Be(4);
            updated2.Error.Should().Be("fatal");
        }
        
        Func<Task> act = async () => await repo.MarkAsFailedAsync(null!, "err");
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ReclaimStaleMessagesAsync_Should_Revert_To_Pending()
    {
        var dbName = Guid.NewGuid().ToString();
        var sp = CreateServiceProvider(dbName);
        var repo = sp.GetRequiredService<IOutboxRepository>();
        var msgId = Guid.NewGuid();

        using (var scope = sp.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            // Create a message that is stale (CreatedAt 2 hours ago, State = 1)
            dbContext.Messages.Add(new OutboxMessageEntity { Id = msgId, MessageType = "A", Payload = Array.Empty<byte>(), HeadersJson = "{}", CreatedAt = DateTimeOffset.UtcNow.AddHours(-2), State = 1 });
            await dbContext.SaveChangesAsync();
        }

        var reclaimed = await repo.ReclaimStaleMessagesAsync(TimeSpan.FromHours(1));
        reclaimed.Should().Be(1);

        using (var scope = sp.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var updated = await dbContext.Messages.FindAsync(msgId);
            updated!.State.Should().Be(0);
        }
        
        // Zero stale messages
        var zero = await repo.ReclaimStaleMessagesAsync(TimeSpan.FromHours(1));
        zero.Should().Be(0);
    }

    [Fact]
    public async Task IdempotencyRepository_PurgeExpiredRecordsAsync_Should_Remove_Old_Records()
    {
        var dbName = Guid.NewGuid().ToString();
        var sp = CreateServiceProvider(dbName);
        var repo = new EntityFrameworkCoreIdempotencyRepository<TestDbContext>(sp);

        using (var scope = sp.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            dbContext.IdempotencyRecords.Add(new IdempotencyRecordEntity { MessageId = "m1", ConsumerId = "c1", ProcessedAt = DateTimeOffset.UtcNow.AddDays(-2) });
            dbContext.IdempotencyRecords.Add(new IdempotencyRecordEntity { MessageId = "m2", ConsumerId = "c2", ProcessedAt = DateTimeOffset.UtcNow });
            await dbContext.SaveChangesAsync();
        }

        await repo.PurgeExpiredRecordsAsync(DateTimeOffset.UtcNow.AddDays(-1));

        using (var scope = sp.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var count = await dbContext.IdempotencyRecords.CountAsync();
            count.Should().Be(1);
        }

        // Test empty purge doesn't fail
        await repo.PurgeExpiredRecordsAsync(DateTimeOffset.UtcNow.AddDays(-10));
    }

    [Fact]
    public void DeadLetterRepository_IsFirstPartyImplementation_Should_Be_True()
    {
        var dbName = Guid.NewGuid().ToString();
        var sp = CreateServiceProvider(dbName);
        var repo = new EntityFrameworkCoreDeadLetterRepository<TestDbContext>(sp);

        repo.IsFirstPartyImplementation.Should().BeTrue();
    }

    [Fact]
    public async Task DeadLetterRepository_Should_Handle_CRUD()
    {
        var dbName = Guid.NewGuid().ToString();
        var sp = CreateServiceProvider(dbName);
        var repo = new EntityFrameworkCoreDeadLetterRepository<TestDbContext>(sp);

        var dlq = new DeadLetterMessage(Guid.NewGuid(), Guid.NewGuid(), "type", Array.Empty<byte>(), "corr", "caus", Array.Empty<byte>(), DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-2), 5, "reason", "error");
        
        // Insert with transaction
        using (var scope = sp.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await repo.InsertAsync(dlq, null!);
        }
        
        // Insert without transaction
        var dlq2 = new DeadLetterMessage(Guid.NewGuid(), Guid.NewGuid(), "type", Array.Empty<byte>(), "corr", "caus", Array.Empty<byte>(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 5, "reason", "error2");
        await repo.InsertAsync(dlq2, null);

        // Get (include exactly equal to After)
        var items = await repo.GetAsync(100, DateTimeOffset.UtcNow.AddDays(-3));
        items.Should().HaveCount(2);
        
        var afterExact = await repo.GetAsync(100, dlq.DeadLetteredAt);
        afterExact.Should().HaveCount(1); // Since the query uses > after.Value, it excludes dlq

        // Delete
        await repo.DeleteAsync(dlq.Id);
        
        var itemsAfterDelete = await repo.GetAsync();
        itemsAfterDelete.Should().HaveCount(1);
        
        // Purge
        await repo.PurgeAsync(DateTimeOffset.UtcNow.AddDays(1));
        var itemsAfterPurge = await repo.GetAsync();
        itemsAfterPurge.Should().BeEmpty();

        // Empty Purge doesn't fail
        await repo.PurgeAsync(DateTimeOffset.UtcNow.AddDays(-10));
    }

    [Fact]
    public void ApplyOutboxEntityConfigurations_Should_Configure_Entities_Correctly()
    {
        using var dbContext = CreateDbContext(Guid.NewGuid().ToString());
        var model = dbContext.Model;

        var outboxEntity = model.FindEntityType(typeof(OutboxMessageEntity));
        outboxEntity.Should().NotBeNull();
        outboxEntity!.GetTableName().Should().Be("messages");
        outboxEntity.GetSchema().Should().Be("outbox");
        outboxEntity.FindPrimaryKey()!.Properties.Select(p => p.Name).Should().BeEquivalentTo("Id");
        outboxEntity.FindProperty("Id")!.ValueGenerated.Should().Be(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never);
        outboxEntity.FindProperty("MessageType")!.GetColumnName().Should().Be("type");
        outboxEntity.FindProperty("MessageType")!.GetMaxLength().Should().Be(255);
        outboxEntity.FindProperty("MessageType")!.IsNullable.Should().BeFalse();
        outboxEntity.FindProperty("Payload")!.GetColumnName().Should().Be("payload");
        outboxEntity.FindProperty("CorrelationId")!.GetColumnName().Should().Be("correlation_id");
        outboxEntity.FindProperty("CausationId")!.GetColumnName().Should().Be("causation_id");
        outboxEntity.FindProperty("HeadersJson")!.GetColumnName().Should().Be("headers_json");
        outboxEntity.FindProperty("HeadersJson")!.GetDefaultValue().Should().Be("{}");
        outboxEntity.FindProperty("CreatedAt")!.GetColumnName().Should().Be("created_at");
        outboxEntity.FindProperty("ProcessedAt")!.GetColumnName().Should().Be("processed_at");
        outboxEntity.FindProperty("DeliverAt")!.GetColumnName().Should().Be("deliver_at");
        outboxEntity.FindProperty("State")!.GetColumnName().Should().Be("state");
        outboxEntity.FindProperty("RetryCount")!.GetColumnName().Should().Be("retry_count");
        outboxEntity.FindProperty("Error")!.GetColumnName().Should().Be("error");
        
        var stateIndex = outboxEntity.GetIndexes().FirstOrDefault(i => i.Properties.Count == 2 && i.Properties[0].Name == "State" && i.Properties[1].Name == "CreatedAt");
        stateIndex.Should().NotBeNull();
        stateIndex!.GetDatabaseName().Should().Be("idx_outbox_messages_state_created");

        var dlqEntity = model.FindEntityType(typeof(DeadLetterMessageEntity));
        dlqEntity.Should().NotBeNull();
        dlqEntity!.GetTableName().Should().Be("dead_letters");
        dlqEntity.GetSchema().Should().Be("outbox");
        dlqEntity.FindPrimaryKey()!.Properties.Select(p => p.Name).Should().BeEquivalentTo("Id");
        dlqEntity.FindProperty("Id")!.ValueGenerated.Should().Be(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never);
        dlqEntity.FindProperty("OriginalMessageId")!.GetColumnName().Should().Be("original_message_id");
        dlqEntity.FindProperty("MessageType")!.GetColumnName().Should().Be("message_type");
        dlqEntity.FindProperty("Payload")!.GetColumnName().Should().Be("payload");
        dlqEntity.FindProperty("CorrelationId")!.GetColumnName().Should().Be("correlation_id");
        dlqEntity.FindProperty("CausationId")!.GetColumnName().Should().Be("causation_id");
        dlqEntity.FindProperty("HeadersJson")!.GetColumnName().Should().Be("headers_json");
        dlqEntity.FindProperty("HeadersJson")!.GetDefaultValue().Should().Be("{}");
        dlqEntity.FindProperty("CreatedAt")!.GetColumnName().Should().Be("created_at");
        dlqEntity.FindProperty("DeadLetteredAt")!.GetColumnName().Should().Be("dead_lettered_at");
        dlqEntity.FindProperty("RetryCount")!.GetColumnName().Should().Be("retry_count");
        dlqEntity.FindProperty("Reason")!.GetColumnName().Should().Be("reason");
        dlqEntity.FindProperty("LastError")!.GetColumnName().Should().Be("last_error");

        var idempotencyEntity = model.FindEntityType(typeof(IdempotencyRecordEntity));
        idempotencyEntity.Should().NotBeNull();
        idempotencyEntity!.GetTableName().Should().Be("idempotency");
        idempotencyEntity.GetSchema().Should().Be("outbox");
        idempotencyEntity.FindPrimaryKey()!.Properties.Select(p => p.Name).Should().BeEquivalentTo("MessageId", "ConsumerId");
        idempotencyEntity.FindProperty("MessageId")!.GetColumnName().Should().Be("message_id");
        idempotencyEntity.FindProperty("ConsumerId")!.GetColumnName().Should().Be("consumer_id");
        idempotencyEntity.FindProperty("ProcessedAt")!.GetColumnName().Should().Be("processed_at");
    }

    [Fact]
    public void AddOutboxEntityFrameworkCore_Should_Register_Repositories()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(opts => opts.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddOutboxEntityFrameworkCore<TestDbContext>();
        
        var sp = services.BuildServiceProvider();
        sp.GetRequiredService<IOutboxRepository>().Should().BeOfType<EntityFrameworkCoreOutboxRepository<TestDbContext>>();
        sp.GetRequiredService<IIdempotencyRepository>().Should().BeOfType<EntityFrameworkCoreIdempotencyRepository<TestDbContext>>();
        sp.GetRequiredService<IDeadLetterRepository>().Should().BeOfType<EntityFrameworkCoreDeadLetterRepository<TestDbContext>>();

        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IOutbox));
        descriptor.Should().NotBeNull();
        descriptor!.ImplementationType.Should().Be<DefaultOutbox>();
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }
    [Fact]
    public async Task GetPendingCountAsync_Should_Return_Count()
    {
        var dbName = Guid.NewGuid().ToString();
        var sp = CreateServiceProvider(dbName);
        var repo = sp.GetRequiredService<IOutboxRepository>();
        
        var count = await repo.GetPendingCountAsync();
        count.Should().Be(0);

        using (var scope = sp.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            dbContext.Messages.Add(new OutboxMessageEntity { Id = Guid.NewGuid(), MessageType = "A", Payload = Array.Empty<byte>(), HeadersJson = "{}", CreatedAt = DateTimeOffset.UtcNow, State = 0 }); // pending
            dbContext.Messages.Add(new OutboxMessageEntity { Id = Guid.NewGuid(), MessageType = "A", Payload = Array.Empty<byte>(), HeadersJson = "{}", CreatedAt = DateTimeOffset.UtcNow, State = 3 }); // retrying
            dbContext.Messages.Add(new OutboxMessageEntity { Id = Guid.NewGuid(), MessageType = "A", Payload = Array.Empty<byte>(), HeadersJson = "{}", CreatedAt = DateTimeOffset.UtcNow, State = 1 }); // processing
            await dbContext.SaveChangesAsync();
        }
        
        count = await repo.GetPendingCountAsync();
        count.Should().Be(2);
    }

    [Fact]
    public void IdempotencyRecordEntity_ToModel_Should_Map_Properties()
    {
        var entity = new IdempotencyRecordEntity
        {
            MessageId = "msg-123",
            ConsumerId = "cons-456",
            ProcessedAt = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };

        var model = entity.ToModel();

        model.MessageId.Should().Be("msg-123");
        model.ConsumerId.Should().Be("cons-456");
        model.ProcessedAt.Should().Be(entity.ProcessedAt);
    }

}



