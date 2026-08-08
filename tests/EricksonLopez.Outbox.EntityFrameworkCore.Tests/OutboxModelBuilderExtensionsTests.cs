using System;
using System.Linq;
using AwesomeAssertions;
using EricksonLopez.Outbox.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace EricksonLopez.Outbox.EntityFrameworkCore.Tests;

public class OutboxModelBuilderExtensionsTests
{
    private sealed class NoCacheModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => Guid.NewGuid();
    }

    private sealed class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

        public DbSet<OutboxMessageEntity> Messages => Set<OutboxMessageEntity>();
        public DbSet<IdempotencyRecordEntity> IdempotencyRecords => Set<IdempotencyRecordEntity>();
        public DbSet<DeadLetterMessageEntity> DeadLetters => Set<DeadLetterMessageEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ApplyOutboxEntityConfigurations("test_schema");
        }
    }

    [Fact]
    public void ApplyOutboxEntityConfigurations_Should_Configure_OutboxMessageEntity()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ReplaceService<IModelCacheKeyFactory, NoCacheModelCacheKeyFactory>()
            .Options;
        
        using var context = new TestDbContext(options);
        var model = context.Model;
        var entityType = model.GetEntityTypes().Single(e => e.ClrType == typeof(OutboxMessageEntity));

        entityType.Should().NotBeNull();
        entityType.GetTableName().Should().Be("messages");
        entityType.GetSchema().Should().Be("test_schema");

        var key = entityType.GetKeys().Single(k => k.IsPrimaryKey());
        key.Should().NotBeNull();
        key.Properties.Single().Name.Should().Be("Id");

        var idProp = entityType.GetProperty("Id");
        idProp.ValueGenerated.Should().Be(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never);

        var typeProp = entityType.GetProperty("MessageType");
        typeProp.GetColumnName().Should().Be("type");
        typeProp.GetMaxLength().Should().Be(255);
        typeProp.IsNullable.Should().BeFalse();

        var payloadProp = entityType.GetProperty("Payload");
        payloadProp.GetColumnName().Should().Be("payload");
        payloadProp.IsNullable.Should().BeFalse();

        var corrProp = entityType.GetProperty("CorrelationId");
        corrProp.GetColumnName().Should().Be("correlation_id");
        corrProp.GetMaxLength().Should().Be(255);

        var causProp = entityType.GetProperty("CausationId");
        causProp.GetColumnName().Should().Be("causation_id");
        causProp.GetMaxLength().Should().Be(255);

        var headersProp = entityType.GetProperty("HeadersJson");
        headersProp.GetColumnName().Should().Be("headers_json");
        headersProp.GetDefaultValue().Should().Be("{}");

        var createdProp = entityType.GetProperty("CreatedAt");
        createdProp.GetColumnName().Should().Be("created_at");
        createdProp.IsNullable.Should().BeFalse();

        var processedProp = entityType.GetProperty("ProcessedAt");
        processedProp.GetColumnName().Should().Be("processed_at");

        var deliverProp = entityType.GetProperty("DeliverAt");
        deliverProp.GetColumnName().Should().Be("deliver_at");

        var stateProp = entityType.GetProperty("State");
        stateProp.GetColumnName().Should().Be("state");
        stateProp.IsNullable.Should().BeFalse();

        var retriesProp = entityType.GetProperty("RetryCount");
        retriesProp.GetColumnName().Should().Be("retry_count");
        retriesProp.IsNullable.Should().BeFalse();

        var errProp = entityType.GetProperty("Error");
        errProp.GetColumnName().Should().Be("error");

        var indexes = entityType.GetIndexes();
        indexes.Should().Contain(i => i.Properties.Count == 2 && i.Properties[0].Name == "State" && i.Properties[1].Name == "CreatedAt");
        var idx = indexes.Single(i => i.Properties.Count == 2 && i.Properties[0].Name == "State" && i.Properties[1].Name == "CreatedAt");
        idx.GetDatabaseName().Should().Be("idx_outbox_messages_state_created");
    }

    [Fact]
    public void ApplyOutboxEntityConfigurations_Should_Configure_IdempotencyRecordEntity()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ReplaceService<IModelCacheKeyFactory, NoCacheModelCacheKeyFactory>()
            .Options;
        
        using var context = new TestDbContext(options);
        var model = context.Model;
        var entityType = model.GetEntityTypes().Single(e => e.ClrType == typeof(IdempotencyRecordEntity));

        entityType.Should().NotBeNull();
        entityType.GetTableName().Should().Be("idempotency");
        entityType.GetSchema().Should().Be("test_schema");

        var key = entityType.GetKeys().Single(k => k.IsPrimaryKey());
        key.Should().NotBeNull();
        key.Properties.Count.Should().Be(2);
        key.Properties.Any(p => p.Name == "MessageId").Should().BeTrue();
        key.Properties.Any(p => p.Name == "ConsumerId").Should().BeTrue();

        var msgIdProp = entityType.GetProperty("MessageId");
        msgIdProp.GetColumnName().Should().Be("message_id");
        msgIdProp.GetMaxLength().Should().Be(255);
        msgIdProp.IsNullable.Should().BeFalse();
        
        var consIdProp = entityType.GetProperty("ConsumerId");
        consIdProp.GetColumnName().Should().Be("consumer_id");
        consIdProp.GetMaxLength().Should().Be(255);
        consIdProp.IsNullable.Should().BeFalse();

        var processedProp = entityType.GetProperty("ProcessedAt");
        processedProp.GetColumnName().Should().Be("processed_at");
        processedProp.IsNullable.Should().BeFalse();
    }

    [Fact]
    public void ApplyOutboxEntityConfigurations_Should_Configure_DeadLetterMessageEntity()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ReplaceService<IModelCacheKeyFactory, NoCacheModelCacheKeyFactory>()
            .Options;
        
        using var context = new TestDbContext(options);
        var model = context.Model;
        var entityType = model.GetEntityTypes().Single(e => e.ClrType == typeof(DeadLetterMessageEntity));

        entityType.Should().NotBeNull();
        entityType.GetTableName().Should().Be("dead_letters");
        entityType.GetSchema().Should().Be("test_schema");

        var key = entityType.GetKeys().Single(k => k.IsPrimaryKey());
        key.Should().NotBeNull();
        key.Properties.Single().Name.Should().Be("Id");

        var idProp = entityType.GetProperty("Id");
        idProp.ValueGenerated.Should().Be(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never);

        var origMsgIdProp = entityType.GetProperty("OriginalMessageId");
        origMsgIdProp.GetColumnName().Should().Be("original_message_id");
        origMsgIdProp.IsNullable.Should().BeFalse();

        var typeProp = entityType.GetProperty("MessageType");
        typeProp.GetColumnName().Should().Be("message_type");
        typeProp.GetMaxLength().Should().Be(255);
        typeProp.IsNullable.Should().BeFalse();

        var payloadProp = entityType.GetProperty("Payload");
        payloadProp.GetColumnName().Should().Be("payload");
        payloadProp.IsNullable.Should().BeFalse();

        var corrProp = entityType.GetProperty("CorrelationId");
        corrProp.GetColumnName().Should().Be("correlation_id");
        corrProp.GetMaxLength().Should().Be(255);

        var causProp = entityType.GetProperty("CausationId");
        causProp.GetColumnName().Should().Be("causation_id");
        causProp.GetMaxLength().Should().Be(255);

        var headersProp = entityType.GetProperty("HeadersJson");
        headersProp.GetColumnName().Should().Be("headers_json");
        headersProp.GetDefaultValue().Should().Be("{}");

        var createdProp = entityType.GetProperty("CreatedAt");
        createdProp.GetColumnName().Should().Be("created_at");
        createdProp.IsNullable.Should().BeFalse();

        var deadLetteredProp = entityType.GetProperty("DeadLetteredAt");
        deadLetteredProp.GetColumnName().Should().Be("dead_lettered_at");
        deadLetteredProp.IsNullable.Should().BeFalse();

        var retriesProp = entityType.GetProperty("RetryCount");
        retriesProp.GetColumnName().Should().Be("retry_count");
        retriesProp.IsNullable.Should().BeFalse();

        var reasonProp = entityType.GetProperty("Reason");
        reasonProp.GetColumnName().Should().Be("reason");
        reasonProp.GetMaxLength().Should().Be(500);

        var lastErrorProp = entityType.GetProperty("LastError");
        lastErrorProp.GetColumnName().Should().Be("last_error");
    }
}
