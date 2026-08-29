// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EricksonLopez.Outbox.Storage.MongoDb;

/// <summary>
/// Provides a MongoDB implementation of <see cref="IOutboxRepository"/> using native BSON documents for AOT safety.
/// </summary>
public sealed class MongoDbOutboxRepository : IOutboxRepository
{
    private static readonly int[] PendingAndFailedStates = [0, 3];
    private static readonly byte[] EmptyBytes = [];

    private readonly IMongoCollection<BsonDocument> _collection;
    private readonly OutboxRuntimeOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="MongoDbOutboxRepository"/> class.
    /// </summary>
    /// <param name="database">The MongoDB database instance.</param>
    /// <param name="options">Optional outbox runtime options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="database"/> is <see langword="null"/>.</exception>
    public MongoDbOutboxRepository(IMongoDatabase database, IOptions<OutboxRuntimeOptions>? options = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        _options = options?.Value ?? new OutboxRuntimeOptions();
        // Stryker disable once all 
        var collectionName = string.IsNullOrEmpty(_options.TableName) || _options.TableName == "outbox_messages"
            ? "outbox_messages"
            : _options.TableName;
        _collection = database.GetCollection<BsonDocument>(collectionName);
    }

    /// <inheritdoc/>
    public async ValueTask InsertAsync(
        OutboxMessage record,
        IOutboxTransactionContext transaction,
        CancellationToken cancellationToken = default)
    {
        var doc = ToBsonDocument(record);
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
    public async ValueTask InsertBatchAsync(
        ReadOnlyMemory<OutboxMessage> records,
        IOutboxTransactionContext transaction,
        CancellationToken cancellationToken = default)
    {
        if (records.IsEmpty) return;

        var docs = new List<BsonDocument>(records.Length);
        for (int i = 0; i < records.Length; i++)
        {
            docs.Add(ToBsonDocument(records.Span[i]));
        }

        if (transaction is MongoDbTransactionContext mongoTx)
        {
            await _collection.InsertManyAsync(mongoTx.Session, docs, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _collection.InsertManyAsync(docs, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<OutboxMessage>> FetchPendingAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.In("state", PendingAndFailedStates),
            Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Eq("deliver_at", BsonNull.Value),
                Builders<BsonDocument>.Filter.Exists("deliver_at", false),
                Builders<BsonDocument>.Filter.Lte("deliver_at", now)
            )
        );

        var sort = Builders<BsonDocument>.Sort.Ascending("created_at").Ascending("_id");

        var update = Builders<BsonDocument>.Update
            .Set("state", 1)
            .Set("updated_at", now);

        var list = new List<OutboxMessage>(batchSize);

        for (int i = 0; i < batchSize; i++)
        {
            var doc = await _collection.FindOneAndUpdateAsync(
                filter,
                update,
                new FindOneAndUpdateOptions<BsonDocument> { Sort = sort, ReturnDocument = ReturnDocument.After },
                cancellationToken).ConfigureAwait(false);

            if (doc == null)
                break;

            list.Add(FromBsonDocument(doc));
        }

        return list;
    }

    /// <inheritdoc/>
    public async ValueTask MarkAsDispatchedAsync(
        IReadOnlyList<OutboxMessage> messages,
        CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0) return;

        var ids = messages.Select(m => m.Id.ToString()).ToList();
        var filter = Builders<BsonDocument>.Filter.In("_id", ids);

        if (_options.DeleteOnDispatch)
        {
            await _collection.DeleteManyAsync(filter, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var update = Builders<BsonDocument>.Update
                .Set("state", 2)
                .Set("processed_at", DateTime.UtcNow)
                .Set("updated_at", DateTime.UtcNow);
            await _collection.UpdateManyAsync(filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async ValueTask MarkAsFailedAsync(
        IReadOnlyList<OutboxMessage> messages,
        string error,
        bool isDeadLetter = false,
        CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0) return;

        var ids = messages.Select(m => m.Id.ToString()).ToList();
        var filter = Builders<BsonDocument>.Filter.In("_id", ids);
        var targetState = isDeadLetter ? 4 : 3;

        var update = Builders<BsonDocument>.Update
            .Set("state", targetState)
            .Set("error", error)
            .Set("updated_at", DateTime.UtcNow)
            .Inc("retry_count", 1);

        await _collection.UpdateManyAsync(filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<int> ReclaimStaleMessagesAsync(
        TimeSpan staleTimeout,
        CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - staleTimeout;
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("state", 1),
            Builders<BsonDocument>.Filter.Lte("updated_at", cutoff)
        );

        var update = Builders<BsonDocument>.Update
            .Set("state", 0)
            .Set("updated_at", DateTime.UtcNow);

        var result = await _collection.UpdateManyAsync(filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
        return (int)result.ModifiedCount;
    }

    /// <inheritdoc/>
    public async ValueTask<long> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.In("state", PendingAndFailedStates);
        return await _collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<OutboxMessage?> GetMessageAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("_id", id.ToString());
        var doc = await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return doc == null ? null : FromBsonDocument(doc);
    }

    /// <inheritdoc/>
    public async ValueTask<int> PurgeDispatchedMessagesAsync(
        DateTimeOffset cutoff,
        int batchSize = 1000,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("state", 2),
            Builders<BsonDocument>.Filter.Lte("processed_at", cutoff.UtcDateTime)
        );

        var result = await _collection.DeleteManyAsync(filter, cancellationToken).ConfigureAwait(false);
        return (int)result.DeletedCount;
    }

    private static BsonDocument ToBsonDocument(OutboxMessage msg)
    {
        var doc = new BsonDocument
        {
            ["_id"] = msg.Id.ToString(),
            ["message_type"] = msg.MessageType,
            ["payload"] = msg.Payload.ToArray(),
            ["correlation_id"] = msg.CorrelationId ?? BsonNull.Value.ToString(),
            ["causation_id"] = msg.CausationId ?? BsonNull.Value.ToString(),
            ["headers"] = msg.Headers.ToArray(),
            ["created_at"] = msg.CreatedAt.UtcDateTime,
            ["state"] = (int)msg.Status,
            ["retry_count"] = msg.RetryCount
        };

        if (msg.DeliverAt.HasValue)
            doc["deliver_at"] = msg.DeliverAt.Value.UtcDateTime;

        if (msg.ProcessedAt.HasValue)
            doc["processed_at"] = msg.ProcessedAt.Value.UtcDateTime;

        if (!string.IsNullOrEmpty(msg.Error))
            doc["error"] = msg.Error;

        if (!string.IsNullOrEmpty(msg.TenantId))
            doc["tenant_id"] = msg.TenantId;

        return doc;
    }

    private static OutboxMessage FromBsonDocument(BsonDocument doc)
    {
        var id = Guid.Parse(doc["_id"].AsString);
        var messageType = doc.Contains("message_type") ? doc["message_type"].AsString : string.Empty;
        var payloadBytes = doc.Contains("payload") && doc["payload"].IsBsonBinaryData
            ? doc["payload"].AsBsonBinaryData.Bytes
            : EmptyBytes;

        string? correlationId = doc.Contains("correlation_id") && !doc["correlation_id"].IsBsonNull
            ? doc["correlation_id"].AsString
            : null;

        string? causationId = doc.Contains("causation_id") && !doc["causation_id"].IsBsonNull
            ? doc["causation_id"].AsString
            : null;

        var headersBytes = doc.Contains("headers") && doc["headers"].IsBsonBinaryData
            ? doc["headers"].AsBsonBinaryData.Bytes
            : EmptyBytes;

        var createdAt = doc.Contains("created_at")
            ? new DateTimeOffset(doc["created_at"].ToUniversalTime(), TimeSpan.Zero)
            : DateTimeOffset.UtcNow;

        DateTimeOffset? processedAt = doc.Contains("processed_at") && !doc["processed_at"].IsBsonNull
            ? new DateTimeOffset(doc["processed_at"].ToUniversalTime(), TimeSpan.Zero)
            : null;

        DateTimeOffset? deliverAt = doc.Contains("deliver_at") && !doc["deliver_at"].IsBsonNull
            ? new DateTimeOffset(doc["deliver_at"].ToUniversalTime(), TimeSpan.Zero)
            : null;

        var state = doc.Contains("state") ? (OutboxMessageStatus)doc["state"].AsInt32 : OutboxMessageStatus.Pending;
        var retryCount = doc.Contains("retry_count") ? doc["retry_count"].AsInt32 : 0;
        string? error = doc.Contains("error") && !doc["error"].IsBsonNull ? doc["error"].AsString : null;

        string? tenantId = doc.Contains("tenant_id") && !doc["tenant_id"].IsBsonNull
            ? doc["tenant_id"].AsString
            : null;

        return new OutboxMessage(
            id,
            messageType,
            payloadBytes,
            correlationId,
            causationId,
            headersBytes,
            createdAt,
            processedAt,
            deliverAt,
            state,
            retryCount,
            error)
        {
            TenantId = tenantId
        };
    }
}
