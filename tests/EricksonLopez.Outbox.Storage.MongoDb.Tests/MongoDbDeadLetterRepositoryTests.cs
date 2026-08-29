// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Storage.MongoDb;
using MongoDB.Bson;
using MongoDB.Driver;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Storage.MongoDb;

public class MongoDbDeadLetterRepositoryTests
{
    private readonly IMongoDatabase _database;
    private readonly IMongoCollection<BsonDocument> _collection;

    public MongoDbDeadLetterRepositoryTests()
    {
        _database = Substitute.For<IMongoDatabase>();
        _collection = Substitute.For<IMongoCollection<BsonDocument>>();
        _database.GetCollection<BsonDocument>("dead_letter_messages", Arg.Any<MongoCollectionSettings>())
            .Returns(_collection);
    }

    [Fact]
    public void IsFirstPartyImplementation_ReturnsTrue()
    {
        var repo = new MongoDbDeadLetterRepository(_database);
        repo.IsFirstPartyImplementation.Should().BeTrue();
    }

    [Fact]
    public void Constructor_WithNullDatabase_ThrowsArgumentNullException()
    {
        var act = () => new MongoDbDeadLetterRepository(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("database");
    }

    [Fact]
    public async Task InsertAsync_WithoutTransaction_InsertsBsonDocumentWithAllFields()
    {
        var repo = new MongoDbDeadLetterRepository(_database);
        var created = DateTimeOffset.UtcNow.AddMinutes(-10);
        var deadLettered = DateTimeOffset.UtcNow;
        var dlqMessage = new DeadLetterMessage(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "order.failed",
            new byte[] { 1, 2, 3 },
            "corr-1",
            "caus-1",
            System.Text.Encoding.UTF8.GetBytes("{\"h\":1}"),
            created,
            deadLettered,
            3,
            "Max retries exceeded",
            "Timeout connecting to broker");

        await repo.InsertAsync(dlqMessage, null, CancellationToken.None);

        await _collection.Received(1).InsertOneAsync(
            Arg.Is<BsonDocument>(doc =>
                doc["_id"].AsString == dlqMessage.Id.ToString() &&
                doc["original_message_id"].AsString == dlqMessage.OriginalMessageId.ToString() &&
                doc["message_type"].AsString == "order.failed" &&
                doc["payload"].AsBsonBinaryData.Bytes.Length == 3 &&
                doc["correlation_id"].AsString == "corr-1" &&
                doc["causation_id"].AsString == "caus-1" &&
                doc["headers"].AsBsonBinaryData.Bytes.Length == 7 &&
                doc.Contains("created_at") &&
                doc.Contains("dead_lettered_at") &&
                doc["retry_count"].AsInt32 == 3 &&
                doc["reason"].AsString == "Max retries exceeded" &&
                doc["last_error"].AsString == "Timeout connecting to broker"),
            Arg.Any<InsertOneOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InsertAsync_WithMongoDbTransactionContext_InsertsWithSession()
    {
        var repo = new MongoDbDeadLetterRepository(_database);
        var dlqMessage = new DeadLetterMessage(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "order.failed",
            ReadOnlyMemory<byte>.Empty,
            null,
            null,
            ReadOnlyMemory<byte>.Empty,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            5,
            "Poison message",
            null);

        var session = Substitute.For<IClientSessionHandle>();
        var mongoTx = new MongoDbTransactionContext(session);

        await repo.InsertAsync(dlqMessage, mongoTx, CancellationToken.None);

        await _collection.Received(1).InsertOneAsync(
            session,
            Arg.Is<BsonDocument>(doc =>
                doc["_id"].AsString == dlqMessage.Id.ToString() &&
                doc["reason"].AsString == "Poison message" &&
                doc["correlation_id"].AsString == BsonNull.Value.ToString() &&
                doc["causation_id"].AsString == BsonNull.Value.ToString() &&
                doc["last_error"].AsString == BsonNull.Value.ToString()),
            Arg.Any<InsertOneOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_CallsDeleteOneWithExactIdFilter()
    {
        var repo = new MongoDbDeadLetterRepository(_database);
        var id = Guid.NewGuid();

        await repo.DeleteAsync(id, CancellationToken.None);

        await _collection.Received(1).DeleteOneAsync(
            Arg.Is<FilterDefinition<BsonDocument>>(f =>
                f.Render()["_id"].AsString == id.ToString()),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PurgeAsync_CallsDeleteManyWithLtFilter()
    {
        var repo = new MongoDbDeadLetterRepository(_database);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);

        await repo.PurgeAsync(cutoff, CancellationToken.None);

        await _collection.Received(1).DeleteManyAsync(
            Arg.Is<FilterDefinition<BsonDocument>>(f =>
                f.Render().Contains("dead_lettered_at") &&
                f.Render()["dead_lettered_at"].AsBsonDocument.Contains("$lt")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_Returns_Mapped_DeadLetterMessages()
    {
        var repo = new MongoDbDeadLetterRepository(_database);
        var id = Guid.NewGuid();
        var origId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var doc1 = new BsonDocument
        {
            ["_id"] = id.ToString(),
            ["original_message_id"] = origId.ToString(),
            ["message_type"] = "order.failed",
            ["payload"] = new BsonBinaryData(new byte[] { 1, 2 }),
            ["correlation_id"] = "c1",
            ["causation_id"] = "c2",
            ["headers"] = new BsonBinaryData(new byte[] { 3, 4 }),
            ["created_at"] = now.AddDays(-10),
            ["dead_lettered_at"] = now,
            ["retry_count"] = 5,
            ["reason"] = "fatal",
            ["last_error"] = "err"
        };

        var doc2 = new BsonDocument
        {
            ["_id"] = Guid.NewGuid().ToString(),
            ["original_message_id"] = Guid.NewGuid().ToString(),
            ["dead_lettered_at"] = now
        };

        var mockCursor = Substitute.For<IAsyncCursor<BsonDocument>>();
        mockCursor.MoveNextAsync(Arg.Any<CancellationToken>()).Returns(true, false);
        mockCursor.Current.Returns(new[] { doc1, doc2 });

        _collection.FindAsync(
            Arg.Is<FilterDefinition<BsonDocument>>(f =>
                f.Render().Contains("dead_lettered_at") &&
                f.Render()["dead_lettered_at"].AsBsonDocument.Contains("$gt")),
            Arg.Is<FindOptions<BsonDocument, BsonDocument>>(opts =>
                opts.Sort != null &&
                opts.Sort.Render()["dead_lettered_at"].AsInt32 == 1 &&
                opts.Limit == 100),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(mockCursor));

        var results = await repo.GetAsync(100, new DateTimeOffset(now.AddDays(-1), TimeSpan.Zero), CancellationToken.None);
        results.Should().HaveCount(2);

        var r1 = results[0];
        r1.Id.Should().Be(id);
        r1.OriginalMessageId.Should().Be(origId);
        r1.MessageType.Should().Be("order.failed");
        r1.Payload.ToArray().Should().Equal(new byte[] { 1, 2 });
        r1.CorrelationId.Should().Be("c1");
        r1.CausationId.Should().Be("c2");
        r1.Headers.ToArray().Should().Equal(new byte[] { 3, 4 });
        r1.CreatedAt.UtcDateTime.Should().BeCloseTo(now.AddDays(-10), TimeSpan.FromSeconds(1));
        r1.DeadLetteredAt.UtcDateTime.Should().BeCloseTo(now, TimeSpan.FromSeconds(1));
        r1.RetryCount.Should().Be(5);
        r1.Reason.Should().Be("fatal");
        r1.LastError.Should().Be("err");

        var r2 = results[1];
        r2.MessageType.Should().Be(string.Empty);
        r2.Payload.ToArray().Should().BeEmpty();
        r2.CorrelationId.Should().BeNull();
        r2.CausationId.Should().BeNull();
        r2.Headers.ToArray().Should().BeEmpty();
        r2.CreatedAt.UtcDateTime.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        r2.RetryCount.Should().Be(0);
        r2.Reason.Should().Be("Unknown");
        r2.LastError.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_WithoutAfter_UsesEmptyFilter()
    {
        var repo = new MongoDbDeadLetterRepository(_database);
        var mockCursor = Substitute.For<IAsyncCursor<BsonDocument>>();
        mockCursor.MoveNextAsync(Arg.Any<CancellationToken>()).Returns(false);
        mockCursor.Current.Returns(Array.Empty<BsonDocument>());

        _collection.FindAsync(
            Arg.Is<FilterDefinition<BsonDocument>>(f => f.Render().ElementCount == 0),
            Arg.Any<FindOptions<BsonDocument, BsonDocument>>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(mockCursor));

        var results = await repo.GetAsync(10, null, CancellationToken.None);
        results.Should().BeEmpty();
    }
}
