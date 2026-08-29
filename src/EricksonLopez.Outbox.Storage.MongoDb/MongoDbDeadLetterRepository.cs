// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EricksonLopez.Outbox.Storage.MongoDb;

/// <summary>
/// Provides a MongoDB implementation of <see cref="IDeadLetterRepository"/>.
/// </summary>
public sealed class MongoDbDeadLetterRepository : IDeadLetterRepository
{
    private static readonly byte[] EmptyBytes = [];
    private readonly IMongoCollection<BsonDocument> _collection;

    /// <inheritdoc/>
    public bool IsFirstPartyImplementation => true;

    /// <summary>
    /// Initializes a new instance of the <see cref="MongoDbDeadLetterRepository"/> class.
    /// </summary>
    /// <param name="database">The MongoDB database instance.</param>
    /// <exception cref="ArgumentNullException"><paramref name="database"/> is <see langword="null"/>.</exception>
    public MongoDbDeadLetterRepository(IMongoDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _collection = database.GetCollection<BsonDocument>("dead_letter_messages");
    }

    /// <inheritdoc/>
    public async ValueTask InsertAsync(
        DeadLetterMessage message,
        IOutboxTransactionContext? transaction = default,
        CancellationToken cancellationToken = default)
    {
        var doc = new BsonDocument
        {
            ["_id"] = message.Id.ToString(),
            ["original_message_id"] = message.OriginalMessageId.ToString(),
            ["message_type"] = message.MessageType,
            ["payload"] = message.Payload.ToArray(),
            ["correlation_id"] = message.CorrelationId ?? BsonNull.Value.ToString(),
            ["causation_id"] = message.CausationId ?? BsonNull.Value.ToString(),
            ["headers"] = message.Headers.ToArray(),
            ["created_at"] = message.CreatedAt.UtcDateTime,
            ["dead_lettered_at"] = message.DeadLetteredAt.UtcDateTime,
            ["retry_count"] = message.RetryCount,
            ["reason"] = message.Reason,
            ["last_error"] = message.LastError ?? BsonNull.Value.ToString()
        };

        if (transaction is MongoDbTransactionContext mongoTx)
        {
            await _collection.InsertOneAsync(mongoTx.Session, doc, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _collection.InsertOneAsync(doc, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<DeadLetterMessage>> GetAsync(
        int limit = 100,
        DateTimeOffset? after = null,
        CancellationToken cancellationToken = default)
    {
        var filter = after.HasValue
            ? Builders<BsonDocument>.Filter.Gt("dead_lettered_at", after.Value.UtcDateTime)
            : Builders<BsonDocument>.Filter.Empty;

        var sort = Builders<BsonDocument>.Sort.Ascending("dead_lettered_at");

        var docs = await _collection.Find(filter)
            .Sort(sort)
            .Limit(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return docs.Select(doc => new DeadLetterMessage(
            Guid.Parse(doc["_id"].AsString),
            Guid.Parse(doc["original_message_id"].AsString),
            doc.Contains("message_type") ? doc["message_type"].AsString : string.Empty,
            doc.Contains("payload") && doc["payload"].IsBsonBinaryData ? doc["payload"].AsBsonBinaryData.Bytes : EmptyBytes,
            doc.Contains("correlation_id") && !doc["correlation_id"].IsBsonNull ? doc["correlation_id"].AsString : null,
            doc.Contains("causation_id") && !doc["causation_id"].IsBsonNull ? doc["causation_id"].AsString : null,
            doc.Contains("headers") && doc["headers"].IsBsonBinaryData ? doc["headers"].AsBsonBinaryData.Bytes : EmptyBytes,
            doc.Contains("created_at") ? new DateTimeOffset(doc["created_at"].ToUniversalTime(), TimeSpan.Zero) : DateTimeOffset.UtcNow,
            new DateTimeOffset(doc["dead_lettered_at"].ToUniversalTime(), TimeSpan.Zero),
            doc.Contains("retry_count") ? doc["retry_count"].AsInt32 : 0,
            doc.Contains("reason") ? doc["reason"].AsString : "Unknown",
            doc.Contains("last_error") && !doc["last_error"].IsBsonNull ? doc["last_error"].AsString : null
        )).ToList();
    }

    /// <inheritdoc/>
    public async ValueTask DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("_id", id.ToString());
        await _collection.DeleteOneAsync(filter, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask PurgeAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.Lt("dead_lettered_at", olderThan.UtcDateTime);
        await _collection.DeleteManyAsync(filter, cancellationToken).ConfigureAwait(false);
    }
}
