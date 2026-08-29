// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Storage.MongoDb;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Storage.MongoDb;

public class MongoDbOutboxRepositoryTests
{
    private readonly IMongoDatabase _database;
    private readonly IMongoCollection<BsonDocument> _collection;

    public MongoDbOutboxRepositoryTests()
    {
        _database = Substitute.For<IMongoDatabase>();
        _collection = Substitute.For<IMongoCollection<BsonDocument>>();
        _database.GetCollection<BsonDocument>(Arg.Any<string>(), Arg.Any<MongoCollectionSettings>())
            .Returns(_collection);
    }

    [Theory]
    [InlineData(null, "messages")]
    [InlineData("", "outbox_messages")]
    [InlineData("outbox_messages", "outbox_messages")]
    [InlineData("custom_outbox_collection", "custom_outbox_collection")]
    public void Constructor_TableNameVariations(string? tableName, string expectedCollectionName)
    {
        var options = tableName == null ? null : Options.Create(new OutboxRuntimeOptions { TableName = tableName });
        _ = new MongoDbOutboxRepository(_database, options);

        _database.Received().GetCollection<BsonDocument>(expectedCollectionName, Arg.Any<MongoCollectionSettings>());
    }

    [Fact]
    public async Task InsertAsync_WithoutTransaction_CallsInsertOneAsyncWithAllFields()
    {
        var repo = new MongoDbOutboxRepository(_database);
        var created = DateTimeOffset.UtcNow.AddMinutes(-5);
        var deliver = DateTimeOffset.UtcNow.AddMinutes(5);
        var msg = new OutboxMessage(
            Guid.NewGuid(),
            "order.created",
            new byte[] { 1, 2, 3 },
            "corr-1",
            "caus-1",
            System.Text.Encoding.UTF8.GetBytes("{\"k\":\"v\"}"),
            created,
            null,
            deliver,
            OutboxMessageStatus.Pending,
            0,
            null)
        {
            TenantId = "tenant-42"
        };

        var tx = Substitute.For<IOutboxTransactionContext>();
        await repo.InsertAsync(msg, tx, CancellationToken.None);

        await _collection.Received(1).InsertOneAsync(
            Arg.Is<BsonDocument>(doc =>
                doc["_id"].AsString == msg.Id.ToString() &&
                doc["message_type"].AsString == "order.created" &&
                doc["payload"].AsBsonBinaryData.Bytes.Length == 3 &&
                doc["headers"].AsBsonBinaryData.Bytes.Length == 9 &&
                doc["correlation_id"].AsString == "corr-1" &&
                doc["causation_id"].AsString == "caus-1" &&
                doc["tenant_id"].AsString == "tenant-42" &&
                doc["state"].AsInt32 == 0 &&
                doc["retry_count"].AsInt32 == 0 &&
                doc.Contains("created_at") &&
                doc.Contains("deliver_at") &&
                !doc.Contains("processed_at") &&
                !doc.Contains("error")),
            Arg.Any<InsertOneOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InsertAsync_WithMongoDbTransactionContext_CallsInsertOneWithSession()
    {
        var repo = new MongoDbOutboxRepository(_database);
        var processed = DateTimeOffset.UtcNow;
        var msg = new OutboxMessage(
            Guid.NewGuid(),
            "order.created",
            new byte[] { 1, 2 },
            null,
            null,
            ReadOnlyMemory<byte>.Empty,
            DateTimeOffset.UtcNow,
            processed,
            null,
            OutboxMessageStatus.Dispatched,
            1,
            "some error");

        var session = Substitute.For<IClientSessionHandle>();
        var mongoTx = new MongoDbTransactionContext(session);

        await repo.InsertAsync(msg, mongoTx, CancellationToken.None);

        await _collection.Received(1).InsertOneAsync(
            session,
            Arg.Is<BsonDocument>(doc =>
                doc["_id"].AsString == msg.Id.ToString() &&
                doc["state"].AsInt32 == 2 &&
                doc["retry_count"].AsInt32 == 1 &&
                doc["error"].AsString == "some error" &&
                doc.Contains("processed_at") &&
                doc["correlation_id"].AsString == BsonNull.Value.ToString() &&
                doc["causation_id"].AsString == BsonNull.Value.ToString() &&
                !doc.Contains("deliver_at") &&
                !doc.Contains("tenant_id")),
            Arg.Any<InsertOneOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InsertBatchAsync_WhenEmpty_ReturnsWithoutCallingCollection()
    {
        var repo = new MongoDbOutboxRepository(_database);
        var tx = Substitute.For<IOutboxTransactionContext>();

        await repo.InsertBatchAsync(ReadOnlyMemory<OutboxMessage>.Empty, tx, CancellationToken.None);

        await _collection.DidNotReceive().InsertManyAsync(
            Arg.Any<IEnumerable<BsonDocument>>(),
            Arg.Any<InsertManyOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InsertBatchAsync_WithoutTransaction_CallsInsertManyAsync()
    {
        var repo = new MongoDbOutboxRepository(_database);
        var msg1 = new OutboxMessage(Guid.NewGuid(), "type1", new byte[] { 1 }, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var msg2 = new OutboxMessage(Guid.NewGuid(), "type2", new byte[] { 2 }, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var messages = new[] { msg1, msg2 };

        var tx = Substitute.For<IOutboxTransactionContext>();
        await repo.InsertBatchAsync(messages, tx, CancellationToken.None);

        await _collection.Received(1).InsertManyAsync(
            Arg.Is<IEnumerable<BsonDocument>>(docs => docs.Count() == 2),
            Arg.Any<InsertManyOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InsertBatchAsync_WithMongoDbTransactionContext_CallsInsertManyWithSession()
    {
        var repo = new MongoDbOutboxRepository(_database);
        var msg = new OutboxMessage(Guid.NewGuid(), "type1", new byte[] { 1 }, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var messages = new[] { msg };

        var session = Substitute.For<IClientSessionHandle>();
        var mongoTx = new MongoDbTransactionContext(session);

        await repo.InsertBatchAsync(messages, mongoTx, CancellationToken.None);

        await _collection.Received(1).InsertManyAsync(
            session,
            Arg.Is<IEnumerable<BsonDocument>>(docs => docs.Count() == 1),
            Arg.Any<InsertManyOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkAsDispatchedAsync_WhenEmpty_DoesNothing()
    {
        var repo = new MongoDbOutboxRepository(_database);
        await repo.MarkAsDispatchedAsync(Array.Empty<OutboxMessage>(), CancellationToken.None);

        await _collection.DidNotReceive().DeleteManyAsync(Arg.Any<FilterDefinition<BsonDocument>>(), Arg.Any<CancellationToken>());
        await _collection.DidNotReceive().UpdateManyAsync(Arg.Any<FilterDefinition<BsonDocument>>(), Arg.Any<UpdateDefinition<BsonDocument>>(), Arg.Any<UpdateOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkAsDispatchedAsync_DeleteOnDispatchFalse_UpdatesStateToDispatched()
    {
        var options = Options.Create(new OutboxRuntimeOptions { DeleteOnDispatch = false });
        var repo = new MongoDbOutboxRepository(_database, options);
        var id = Guid.NewGuid();
        var msg = new OutboxMessage(id, "type", Array.Empty<byte>(), null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, 0, 0, null);

        await repo.MarkAsDispatchedAsync(new[] { msg }, CancellationToken.None);

        await _collection.Received(1).UpdateManyAsync(
            Arg.Is<FilterDefinition<BsonDocument>>(f => f.Render()["_id"]["$in"].AsBsonArray.Contains(id.ToString())),
            Arg.Is<UpdateDefinition<BsonDocument>>(u =>
                u.Render()["$set"]["state"].AsInt32 == 2 &&
                u.Render()["$set"].AsBsonDocument.Contains("processed_at") &&
                u.Render()["$set"].AsBsonDocument.Contains("updated_at")),
            Arg.Any<UpdateOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkAsDispatchedAsync_DeleteOnDispatchTrue_DeletesMessages()
    {
        var options = Options.Create(new OutboxRuntimeOptions { DeleteOnDispatch = true });
        var repo = new MongoDbOutboxRepository(_database, options);
        var id = Guid.NewGuid();
        var msg = new OutboxMessage(id, "type", Array.Empty<byte>(), null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, 0, 0, null);

        await repo.MarkAsDispatchedAsync(new[] { msg }, CancellationToken.None);

        await _collection.Received(1).DeleteManyAsync(
            Arg.Is<FilterDefinition<BsonDocument>>(f => f.Render()["_id"]["$in"].AsBsonArray.Contains(id.ToString())),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkAsFailedAsync_WhenEmpty_DoesNothing()
    {
        var repo = new MongoDbOutboxRepository(_database);
        await repo.MarkAsFailedAsync(Array.Empty<OutboxMessage>(), "error", false, CancellationToken.None);

        await _collection.DidNotReceive().UpdateManyAsync(Arg.Any<FilterDefinition<BsonDocument>>(), Arg.Any<UpdateDefinition<BsonDocument>>(), Arg.Any<UpdateOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkAsFailedAsync_TransientFailure_UpdatesStateToFailed()
    {
        var repo = new MongoDbOutboxRepository(_database);
        var id = Guid.NewGuid();
        var msg = new OutboxMessage(id, "type", Array.Empty<byte>(), null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, 0, 0, null);

        await repo.MarkAsFailedAsync(new[] { msg }, "network timeout", isDeadLetter: false, CancellationToken.None);

        await _collection.Received(1).UpdateManyAsync(
            Arg.Is<FilterDefinition<BsonDocument>>(f => f.Render()["_id"]["$in"].AsBsonArray.Contains(id.ToString())),
            Arg.Is<UpdateDefinition<BsonDocument>>(u =>
                u.Render()["$set"]["state"].AsInt32 == 3 &&
                u.Render()["$set"]["error"].AsString == "network timeout" &&
                u.Render()["$inc"]["retry_count"].AsInt32 == 1 &&
                u.Render()["$set"].AsBsonDocument.Contains("updated_at")),
            Arg.Any<UpdateOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkAsFailedAsync_DeadLetter_UpdatesStateToDeadLetter()
    {
        var repo = new MongoDbOutboxRepository(_database);
        var id = Guid.NewGuid();
        var msg = new OutboxMessage(id, "type", Array.Empty<byte>(), null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, 0, 0, null);

        await repo.MarkAsFailedAsync(new[] { msg }, "fatal serialization error", isDeadLetter: true, CancellationToken.None);

        await _collection.Received(1).UpdateManyAsync(
            Arg.Is<FilterDefinition<BsonDocument>>(f => f.Render()["_id"]["$in"].AsBsonArray.Contains(id.ToString())),
            Arg.Is<UpdateDefinition<BsonDocument>>(u =>
                u.Render()["$set"]["state"].AsInt32 == 4 &&
                u.Render()["$set"]["error"].AsString == "fatal serialization error" &&
                u.Render()["$inc"]["retry_count"].AsInt32 == 1 &&
                u.Render()["$set"].AsBsonDocument.Contains("updated_at")),
            Arg.Any<UpdateOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReclaimStaleMessagesAsync_CallsUpdateManyAndReturnsModifiedCount()
    {
        var repo = new MongoDbOutboxRepository(_database);
        var updateResult = Substitute.For<UpdateResult>();
        updateResult.ModifiedCount.Returns(7);
        var before = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(5));

        _collection.UpdateManyAsync(
            Arg.Is<FilterDefinition<BsonDocument>>(f =>
                f.Render()["state"].AsInt32 == 1 &&
                f.Render()["updated_at"].AsBsonDocument.Contains("$lte") &&
                f.Render()["updated_at"]["$lte"].ToUniversalTime() <= DateTime.UtcNow &&
                f.Render()["updated_at"]["$lte"].ToUniversalTime() >= before.AddSeconds(-1)),
            Arg.Is<UpdateDefinition<BsonDocument>>(u =>
                u.Render()["$set"]["state"].AsInt32 == 0 &&
                u.Render()["$set"].AsBsonDocument.Contains("updated_at")),
            Arg.Any<UpdateOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(updateResult));

        var reclaimed = await repo.ReclaimStaleMessagesAsync(TimeSpan.FromMinutes(5), CancellationToken.None);

        reclaimed.Should().Be(7);
    }

    [Fact]
    public async Task GetPendingCountAsync_CallsCountDocumentsAsyncWithPendingAndFailedFilter()
    {
        var repo = new MongoDbOutboxRepository(_database);
        _collection.CountDocumentsAsync(
            Arg.Is<FilterDefinition<BsonDocument>>(f =>
                f.Render()["state"]["$in"].AsBsonArray.Count == 2 &&
                f.Render()["state"]["$in"].AsBsonArray.Contains(0) &&
                f.Render()["state"]["$in"].AsBsonArray.Contains(3)),
            Arg.Any<CountOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(12L));

        var count = await repo.GetPendingCountAsync(CancellationToken.None);

        count.Should().Be(12L);
    }

    [Fact]
    public async Task PurgeDispatchedMessagesAsync_CallsDeleteManyWithStateAndCutoff()
    {
        var repo = new MongoDbOutboxRepository(_database);
        var deleteResult = Substitute.For<DeleteResult>();
        deleteResult.DeletedCount.Returns(25);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-7);

        _collection.DeleteManyAsync(
            Arg.Is<FilterDefinition<BsonDocument>>(f =>
                f.Render()["state"].AsInt32 == 2 &&
                f.Render()["processed_at"].AsBsonDocument.Contains("$lte")),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(deleteResult));

        var purged = await repo.PurgeDispatchedMessagesAsync(cutoff, 1000, CancellationToken.None);

        purged.Should().Be(25);
    }

    [Fact]
    public async Task FetchPendingAsync_WhenNoDocsReturned_ReturnsEmptyList()
    {
        var repo = new MongoDbOutboxRepository(_database);
        _collection.FindOneAndUpdateAsync(
            Arg.Any<FilterDefinition<BsonDocument>>(),
            Arg.Any<UpdateDefinition<BsonDocument>>(),
            Arg.Any<FindOneAndUpdateOptions<BsonDocument>>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BsonDocument>(null!));

        var result = await repo.FetchPendingAsync(10, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchPendingAsync_FetchesUpToBatchSizeDocs()
    {
        var repo = new MongoDbOutboxRepository(_database);
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var doc1 = new BsonDocument
        {
            ["_id"] = id1.ToString(),
            ["message_type"] = "order.created",
            ["payload"] = new BsonBinaryData(new byte[] { 10, 20 }),
            ["correlation_id"] = "corr-1",
            ["causation_id"] = "caus-1",
            ["headers"] = new BsonBinaryData(new byte[] { 30, 40 }),
            ["created_at"] = now.AddDays(-10),
            ["state"] = 1,
            ["retry_count"] = 0,
            ["tenant_id"] = "t1"
        };
        var doc2 = new BsonDocument
        {
            ["_id"] = id2.ToString(),
            ["message_type"] = "order.shipped",
            ["payload"] = new BsonBinaryData(new byte[] { 50 }),
            ["state"] = 1,
            ["retry_count"] = 1
        };
        var doc3 = new BsonDocument
        {
            ["_id"] = id3.ToString(),
            ["message_type"] = "order.extra",
            ["created_at"] = now
        };

        _collection.FindOneAndUpdateAsync(
            Arg.Is<FilterDefinition<BsonDocument>>(f =>
                f.Render()["state"]["$in"].AsBsonArray.Contains(0) &&
                f.Render()["state"]["$in"].AsBsonArray.Contains(3) &&
                f.Render()["$or"].AsBsonArray.Count == 3 &&
                f.Render()["$or"].AsBsonArray[0]["deliver_at"].BsonType == BsonType.Null &&
                f.Render()["$or"].AsBsonArray[1]["deliver_at"]["$exists"].AsBoolean == false &&
                f.Render()["$or"].AsBsonArray[2]["deliver_at"].AsBsonDocument.Contains("$lte")),
            Arg.Is<UpdateDefinition<BsonDocument>>(u =>
                u.Render()["$set"]["state"].AsInt32 == 1 &&
                u.Render()["$set"].AsBsonDocument.Contains("updated_at")),
            Arg.Is<FindOneAndUpdateOptions<BsonDocument>>(opts =>
                opts.ReturnDocument == ReturnDocument.After &&
                opts.Sort != null &&
                opts.Sort.Render()["created_at"].AsInt32 == 1 &&
                opts.Sort.Render()["_id"].AsInt32 == 1),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BsonDocument>(doc1), Task.FromResult<BsonDocument>(doc2), Task.FromResult<BsonDocument>(doc3));

        var result = await repo.FetchPendingAsync(2, CancellationToken.None);

        result.Should().HaveCount(2);
        var msg1 = result[0];
        msg1.Id.Should().Be(id1);
        msg1.MessageType.Should().Be("order.created");
        msg1.CorrelationId.Should().Be("corr-1");
        msg1.CausationId.Should().Be("caus-1");
        msg1.TenantId.Should().Be("t1");
        msg1.Payload.ToArray().Should().Equal(new byte[] { 10, 20 });
        msg1.Headers.ToArray().Should().Equal(new byte[] { 30, 40 });
        msg1.CreatedAt.UtcDateTime.Should().BeCloseTo(now.AddDays(-10), TimeSpan.FromSeconds(1));
        msg1.Status.Should().Be(OutboxMessageStatus.InFlight);

        var msg2 = result[1];
        msg2.Id.Should().Be(id2);
        msg2.MessageType.Should().Be("order.shipped");
        msg2.CreatedAt.UtcDateTime.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        msg2.Status.Should().Be(OutboxMessageStatus.InFlight);
        msg2.RetryCount.Should().Be(1);
    }

    [Fact]
    public async Task GetMessageAsync_WhenFound_ReturnsMappedOutboxMessage()
    {
        var repo = new MongoDbOutboxRepository(_database);
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var doc = new BsonDocument
        {
            ["_id"] = id.ToString(),
            ["message_type"] = "order.created",
            ["payload"] = new BsonBinaryData(new byte[] { 1, 2 }),
            ["correlation_id"] = "c1",
            ["causation_id"] = "c2",
            ["headers"] = new BsonBinaryData(new byte[] { 3, 4 }),
            ["created_at"] = now,
            ["processed_at"] = now,
            ["deliver_at"] = now,
            ["state"] = 2,
            ["retry_count"] = 1,
            ["error"] = "err",
            ["tenant_id"] = "t1"
        };

        var mockCursor = Substitute.For<IAsyncCursor<BsonDocument>>();
        mockCursor.MoveNextAsync(Arg.Any<CancellationToken>()).Returns(true, false);
        mockCursor.Current.Returns(new[] { doc });

        _collection.FindAsync(
            Arg.Is<FilterDefinition<BsonDocument>>(f => f.Render()["_id"].AsString == id.ToString()),
            Arg.Any<FindOptions<BsonDocument, BsonDocument>>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(mockCursor));

        var msg = await repo.GetMessageAsync(id, CancellationToken.None);

        msg.Should().NotBeNull();
        msg!.Id.Should().Be(id);
        msg.MessageType.Should().Be("order.created");
        msg.CorrelationId.Should().Be("c1");
        msg.CausationId.Should().Be("c2");
        msg.TenantId.Should().Be("t1");
        msg.Status.Should().Be(OutboxMessageStatus.Dispatched);
        msg.RetryCount.Should().Be(1);
        msg.Error.Should().Be("err");
        msg.ProcessedAt.Should().NotBeNull();
        msg.DeliverAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetMessageAsync_WhenNotFound_ReturnsNull()
    {
        var repo = new MongoDbOutboxRepository(_database);
        var mockCursor = Substitute.For<IAsyncCursor<BsonDocument>>();
        mockCursor.MoveNextAsync(Arg.Any<CancellationToken>()).Returns(false);
        mockCursor.Current.Returns(Array.Empty<BsonDocument>());

        _collection.FindAsync(
            Arg.Any<FilterDefinition<BsonDocument>>(),
            Arg.Any<FindOptions<BsonDocument, BsonDocument>>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(mockCursor));

        var msg = await repo.GetMessageAsync(Guid.NewGuid(), CancellationToken.None);

        msg.Should().BeNull();
    }

    [Fact]
    public async Task FetchPendingAsync_WithMinimalDocument_UsesDefaults()
    {
        var repo = new MongoDbOutboxRepository(_database);
        var id = Guid.NewGuid();
        var doc = new BsonDocument
        {
            ["_id"] = id.ToString()
        };

        _collection.FindOneAndUpdateAsync(
            Arg.Any<FilterDefinition<BsonDocument>>(),
            Arg.Any<UpdateDefinition<BsonDocument>>(),
            Arg.Any<FindOneAndUpdateOptions<BsonDocument>>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BsonDocument>(doc), Task.FromResult<BsonDocument>(null!));

        var result = await repo.FetchPendingAsync(1, CancellationToken.None);

        result.Should().HaveCount(1);
        var msg = result[0];
        msg.Id.Should().Be(id);
        msg.MessageType.Should().Be(string.Empty);
        msg.Payload.ToArray().Should().BeEmpty();
        msg.Headers.ToArray().Should().BeEmpty();
        msg.CorrelationId.Should().BeNull();
        msg.CausationId.Should().BeNull();
        msg.TenantId.Should().BeNull();
        msg.DeliverAt.Should().BeNull();
        msg.ProcessedAt.Should().BeNull();
        msg.Error.Should().BeNull();
        msg.Status.Should().Be(OutboxMessageStatus.Pending);
        msg.RetryCount.Should().Be(0);
    }
}
