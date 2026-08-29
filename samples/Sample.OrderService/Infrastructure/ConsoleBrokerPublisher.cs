// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using Microsoft.Extensions.Logging;

namespace Sample.OrderService.Infrastructure;

/// <summary>
/// A dummy Broker Publisher that simply prints to the console.
/// Ideal for running the sample without needing to install RabbitMQ or Kafka.
/// <para>
/// To use RabbitMQ in a real environment, install <c>EricksonLopez.Outbox.RabbitMQ</c>
/// and configure: <c>services.AddSingleton&lt;IBrokerPublisher&gt;(new RabbitMQBrokerPublisher(...))</c>
/// </para>
/// </summary>
public sealed class ConsoleBrokerPublisher : IBrokerPublisher
{
    private readonly ILogger<ConsoleBrokerPublisher> _logger;

    public ConsoleBrokerPublisher(ILogger<ConsoleBrokerPublisher> logger)
    {
        _logger = logger;
    }

    public ValueTask<DispatchResult> PublishAsync<T>(MessageEnvelope<T> message, DispatchContext context) where T : notnull
    {
        _logger.LogInformation(
            "\n[BROKER-PUBLISH-TYPED] \n" +
            "  => Type: {MessageType}\n" +
            "  => CorrelationId: {CorrelationId}\n",
            message.Metadata.MessageType, message.Metadata.CorrelationId);

        return ValueTask.FromResult(DispatchResult.Ok());
    }

    public ValueTask<IReadOnlyList<DispatchResult>> PublishBatchAsync<T>(IReadOnlyList<MessageEnvelope<T>> messages, DispatchContext context) where T : notnull
    {
        var results = new List<DispatchResult>(messages.Count);
        foreach (var msg in messages)
        {
            _logger.LogInformation("[BROKER-PUBLISH-BATCH] Type: {MessageType}", msg.Metadata.MessageType);
            results.Add(DispatchResult.Ok());
        }
        return ValueTask.FromResult<IReadOnlyList<DispatchResult>>(results);
    }

    public ValueTask<DispatchResult> PublishRawAsync(OutboxMessage message, OutboxMessageMetadata metadata, DispatchContext context)
    {
        var payload = System.Text.Encoding.UTF8.GetString(message.Payload.Span);
        _logger.LogInformation(
            "\n[BROKER-PUBLISH-RAW] \n" +
            "  => Type: {MessageType}\n" +
            "  => ID: {MessageId}\n" +
            "  => Payload: {Payload}\n" +
            "  => Created: {CreatedAt}\n",
            message.MessageType, message.Id, payload, message.CreatedAt);

        return ValueTask.FromResult(DispatchResult.Ok());
    }
}





