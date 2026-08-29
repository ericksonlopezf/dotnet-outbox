// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon.SQS;
using Amazon.SQS.Model;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Serialization;

namespace EricksonLopez.Outbox.Brokers.AwsSqs;

/// <summary>
/// Provides a broker publisher implementation that dispatches outbox messages to Amazon Simple Queue Service (SQS).
/// </summary>
public sealed class AwsSqsBrokerPublisher : IBrokerPublisher
{
    private readonly IAmazonSQS _sqsClient;
    private readonly IOutboxSerializer _serializer;
    private readonly string _queueUrl;
    private readonly bool _isFifoQueue;

    /// <summary>
    /// Initializes a new instance of the <see cref="AwsSqsBrokerPublisher"/> class.
    /// </summary>
    /// <param name="sqsClient">The Amazon SQS client that communicates with the service.</param>
    /// <param name="serializer">The serializer that converts message payloads to byte arrays.</param>
    /// <param name="queueUrl">The URL of the target SQS queue.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="sqsClient"/>, <paramref name="serializer"/>, or <paramref name="queueUrl"/> is <see langword="null"/>.
    /// </exception>
    public AwsSqsBrokerPublisher(IAmazonSQS sqsClient, IOutboxSerializer serializer, string queueUrl)
    {
        _sqsClient = sqsClient ?? throw new ArgumentNullException(nameof(sqsClient));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _queueUrl = queueUrl ?? throw new ArgumentNullException(nameof(queueUrl));
        _isFifoQueue = queueUrl.EndsWith(".fifo", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public async ValueTask<DispatchResult> PublishAsync<T>(MessageEnvelope<T> message, DispatchContext context) where T : notnull
    {
        try
        {
            var request = CreateSendMessageRequest(message);
            var response = await _sqsClient.SendMessageAsync(request, context.CancellationToken);
            
            return DispatchResult.Ok();
        }
        catch (AmazonSQSException ex) when (ex.StatusCode >= System.Net.HttpStatusCode.InternalServerError || ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            return DispatchResult.FailAndRetry(ex);
        }
        catch (Exception ex)
        {
            return DispatchResult.FailFatal(ex);
        }
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<DispatchResult>> PublishBatchAsync<T>(IReadOnlyList<MessageEnvelope<T>> messages, DispatchContext context) where T : notnull
    {
        var results = new List<DispatchResult>(messages.Count);
        
        try
        {
            var entries = new List<SendMessageBatchRequestEntry>(messages.Count);
            
            for (int i = 0; i < messages.Count; i++)
            {
                var msg = messages[i];
                var req = CreateSendMessageRequest(msg);
                entries.Add(new SendMessageBatchRequestEntry
                {
                    Id = i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    MessageBody = req.MessageBody,
                    MessageAttributes = req.MessageAttributes,
                    MessageGroupId = req.MessageGroupId,
                    MessageDeduplicationId = req.MessageDeduplicationId
                });
            }

            var batchRequest = new SendMessageBatchRequest
            {
                QueueUrl = _queueUrl,
                Entries = entries
            };

            var response = await _sqsClient.SendMessageBatchAsync(batchRequest, context.CancellationToken);

            // In SQS Batch, some messages might fail while others succeed.
            // For simplicity in this adapter, we will assume success if there are no Failed entries.
            if (response.Failed.Count > 0)
            {
                // This is a partial failure scenario. The robust way is to map individual failures back to their messages.
                // We'll mark all as FailAndRetry to force the outbox to retry the batch (idempotency will protect duplicates).
                throw new InvalidOperationException($"SQS Batch Send failed for {response.Failed.Count} messages.");
            }

            for (int i = 0; i < messages.Count; i++)
            {
                results.Add(DispatchResult.Ok());
            }
        }
        catch (Exception ex)
        {
            // Transient or Fatal mapping for the whole batch
            for (int i = 0; i < messages.Count; i++)
            {
                results.Add(DispatchResult.FailAndRetry(ex));
            }
        }

        return results;
    }

    private SendMessageRequest CreateSendMessageRequest<T>(MessageEnvelope<T> message) where T : notnull
    {
        var payloadBytes = _serializer.Serialize(message.Payload).ToArray();
        // SQS expects strings, so we Base64 encode the binary payload
        var body = Convert.ToBase64String(payloadBytes);

        var request = new SendMessageRequest
        {
            QueueUrl = _queueUrl,
            MessageBody = body,
            MessageAttributes = new Dictionary<string, MessageAttributeValue>()
        };

        if (_isFifoQueue)
        {
            // In FIFO queues, MessageGroupId is mandatory.
            request.MessageGroupId = message.Metadata.CorrelationId ?? "default-group";
            
            // Outbox inherently provides idempotency, but SQS needs a Deduplication ID if content-based deduplication is off.
            // We use a combination of CausationId and CorrelationId or just a random GUID for safety if not provided.
            request.MessageDeduplicationId = message.Metadata.CausationId ?? Guid.NewGuid().ToString();
        }

        if (!string.IsNullOrEmpty(message.Metadata.CorrelationId))
        {
            request.MessageAttributes["CorrelationId"] = new MessageAttributeValue { DataType = "String", StringValue = message.Metadata.CorrelationId };
        }
        
        if (!string.IsNullOrEmpty(message.Metadata.MessageType))
        {
            request.MessageAttributes["MessageType"] = new MessageAttributeValue { DataType = "String", StringValue = message.Metadata.MessageType };
        }

        foreach (var header in message.Metadata.Entries.Span)
        {
            request.MessageAttributes[header.Key] = new MessageAttributeValue { DataType = "String", StringValue = header.Value };
        }

        return request;
    }

    /// <inheritdoc/>
    public async ValueTask<DispatchResult> PublishRawAsync(
        OutboxMessage message,
        OutboxMessageMetadata metadata,
        DispatchContext context)
    {
        try
        {
            var body = System.Text.Encoding.UTF8.GetString(message.Payload.Span);
            var request = new Amazon.SQS.Model.SendMessageRequest
            {
                QueueUrl = _queueUrl,
                MessageBody = body,
                MessageAttributes = new Dictionary<string, MessageAttributeValue>
                {
                    ["MessageType"] = new() { DataType = "String", StringValue = message.MessageType }
                }
            };

            if (metadata.CorrelationId is not null)
                request.MessageAttributes["CorrelationId"] = new() { DataType = "String", StringValue = metadata.CorrelationId };

            foreach (var header in metadata.Entries.Span)
                request.MessageAttributes[header.Key] = new() { DataType = "String", StringValue = header.Value };

            await _sqsClient.SendMessageAsync(request, context.CancellationToken);
            return DispatchResult.Ok();
        }
        catch (Exception ex)
        {
            return DispatchResult.FailAndRetry(ex);
        }
    }
}





