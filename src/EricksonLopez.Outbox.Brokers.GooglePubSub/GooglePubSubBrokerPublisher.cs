using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using EricksonLopez.Outbox;

namespace EricksonLopez.Outbox.Brokers.GooglePubSub;

/// <summary>
/// Publishes outbox messages to Google Cloud Pub/Sub using the official Google.Cloud.PubSub.V1 client.
/// </summary>
/// <remarks>
/// Design decisions:
///   - Topic name is derived from the message type alias to allow environment-specific topic naming conventions.
///   - Message attributes are used for metadata (CorrelationId, CausationId, MessageType).
///     Attributes are indexed by Pub/Sub and support subscription filter expressions.
///   - PublishRawAsync sends the pre-serialized payload directly — avoids double serialization.
///   - Ordering keys are not set by default; enable them per-topic if strict ordering is required.
///
/// Trade-off:
///   + Fully managed — no infrastructure to maintain.
///   + At-least-once delivery with configurable retention.
///   - Topic must be pre-created (Infrastructure as Code ownership).
/// </remarks>
public sealed class GooglePubSubBrokerPublisher : IBrokerPublisher
{
    private readonly PublisherServiceApiClient _client;
    private readonly string _projectId;
    private readonly Func<string, string> _topicNamingStrategy;

    /// <summary>
    /// Initializes a new instance of the <see cref="GooglePubSubBrokerPublisher"/> class.
    /// </summary>
    /// <param name="client">The Google Cloud Pub/Sub publisher service API client.</param>
    /// <param name="projectId">The Google Cloud project identifier containing the target topics.</param>
    /// <param name="topicNamingStrategy">An optional function that derives the topic name from the message type alias. If <see langword="null"/>, a default strategy is used.</param>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="projectId"/> is <see langword="null"/> or white space.</exception>
    public GooglePubSubBrokerPublisher(
        PublisherServiceApiClient client,
        string projectId,
        Func<string, string>? topicNamingStrategy = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _projectId = string.IsNullOrWhiteSpace(projectId)
            ? throw new ArgumentException("projectId cannot be null or empty.", nameof(projectId))
            : projectId;

        // Default: "order.created.v1" → "order-created-v1"
        _topicNamingStrategy = topicNamingStrategy
            ?? (alias => alias.Replace('.', '-').ToLowerInvariant());
    }

    /// <inheritdoc/>
    public async ValueTask<DispatchResult> PublishAsync<T>(
        MessageEnvelope<T> message,
        DispatchContext context) where T : notnull
    {
        // For strongly-typed publish we don't have the serialized payload here.
        // This overload is intended for application-level use; the dispatcher uses PublishRawAsync.
        throw new NotSupportedException(
            "Use PublishRawAsync for dispatcher-initiated publishing. " +
            "Strongly-typed publish via the Outbox stores the message first.");
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<DispatchResult>> PublishBatchAsync<T>(
        IReadOnlyList<MessageEnvelope<T>> messages,
        DispatchContext context) where T : notnull
    {
        throw new NotSupportedException(
            "Use PublishRawAsync for dispatcher-initiated publishing. " +
            "Strongly-typed publish via the Outbox stores the message first.");
    }

    /// <inheritdoc/>
    public async ValueTask<DispatchResult> PublishRawAsync(
        OutboxMessage message,
        MessageMetadata metadata,
        DispatchContext context)
    {
        try
        {
            var topicName = TopicName.FromProjectTopic(
                _projectId,
                _topicNamingStrategy(message.MessageType));

            var pubsubMessage = new PubsubMessage
            {
                Data = ByteString.CopyFrom(message.Payload.ToArray())
            };

            // Propagate metadata as Pub/Sub attributes
            pubsubMessage.Attributes["message_type"] = message.MessageType;

            if (metadata.CorrelationId is not null)
                pubsubMessage.Attributes["correlation_id"] = metadata.CorrelationId;

            if (metadata.CausationId is not null)
                pubsubMessage.Attributes["causation_id"] = metadata.CausationId;

            foreach (var entry in metadata.Entries.Span)
                pubsubMessage.Attributes[entry.Key] = entry.Value;

            await _client.PublishAsync(topicName, new[] { pubsubMessage });

            return DispatchResult.Ok();
        }
        catch (Exception ex)
        {
            return DispatchResult.FailAndRetry(ex);
        }
    }
}
