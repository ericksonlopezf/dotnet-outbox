// Copyright © Erickson Lopez. MIT License.
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace EricksonLopez.Outbox.Tests.Storage.MongoDb;

internal static class MongoTestHelper
{
    private static readonly IBsonSerializer<BsonDocument> DocumentSerializer = BsonSerializer.SerializerRegistry.GetSerializer<BsonDocument>();
    private static readonly RenderArgs<BsonDocument> Args = new(DocumentSerializer, BsonSerializer.SerializerRegistry);

    public static BsonDocument Render(this FilterDefinition<BsonDocument> filter) => filter.Render(Args);
    public static BsonDocument Render(this UpdateDefinition<BsonDocument> update) => update.Render(Args).AsBsonDocument;
    public static BsonDocument Render(this SortDefinition<BsonDocument> sort) => sort.Render(Args);
}
