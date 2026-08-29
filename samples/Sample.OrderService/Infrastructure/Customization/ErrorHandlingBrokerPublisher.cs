// Copyright © Erickson Lopez. MIT License.
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using Microsoft.Extensions.Logging;

namespace Sample.OrderService.Infrastructure.Customization;

/// <summary>
/// Showcase Publisher
/// Demonstrates how an <see cref="IBrokerPublisher"/> should handle errors
/// and return the different states of <see cref="DispatchResult"/>.
/// </summary>
/// <remarks>
/// This publisher doesn't send messages to a real broker. It prints them to the console
/// and simulates random errors to demonstrate the dispatcher's retry policies.
/// </remarks>
public sealed class ErrorHandlingBrokerPublisher : IBrokerPublisher
{
    private readonly ILogger<ErrorHandlingBrokerPublisher> _logger;
    private readonly Random _random = new();

    public ErrorHandlingBrokerPublisher(ILogger<ErrorHandlingBrokerPublisher> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public ValueTask<DispatchResult> PublishRawAsync(
        OutboxMessage message,
        OutboxMessageMetadata metadata,
        DispatchContext context)
    {
        _logger.LogInformation(
            "Attempting to publish message {MessageId} (Type: {Type}, Attempt: {Attempt})",
            message.Id, metadata.MessageType, message.RetryCount + 1);

        // Simulation of scenarios:
        // 1. 70% chance of Success (Ok)
        // 2. 20% chance of Transient Error (FailAndRetry)
        // 3. 10% chance of Permanent Error (FailFatal)

        int chance = _random.Next(1, 101);

        if (chance <= 70)
        {
            // Scenario 1: SUCCESS
            // The message reached the broker correctly.
            // Action: The dispatcher will DELETE the message from the DB (or UPDATE if DeleteOnDispatch=false).
            var payloadStr = Encoding.UTF8.GetString(message.Payload.Span);
            _logger.LogInformation("PUBLISHED successfully: {Payload}", payloadStr);
            
            return ValueTask.FromResult(DispatchResult.Ok());
        }
        else if (chance <= 90)
        {
            // Scenario 2: TRANSIENT ERROR
            // Network timeout, broker unavailable, connection reset.
            // Action: The dispatcher will rollback the internal transaction, increment RetryCount,
            // and apply the exponential backoff to set deliver_at in the future.
            var transientEx = new TimeoutException("Connection to broker timed out.");
            _logger.LogWarning(transientEx, "TRANSIENT FAILURE when publishing.");
            
            return ValueTask.FromResult(DispatchResult.FailAndRetry(transientEx));
        }
        else
        {
            // Scenario 3: FATAL ERROR
            // The message is too large for the broker queue, schema mismatch, unauthorized.
            // Action: The dispatcher will NOT retry. It will mark it as Dead-Letter (state=4) immediately.
            var fatalEx = new InvalidOperationException("Payload exceeds broker's max message size limit.");
            _logger.LogError(fatalEx, "FATAL FAILURE when publishing.");
            
            return ValueTask.FromResult(DispatchResult.FailFatal(fatalEx));
        }
    }
}




