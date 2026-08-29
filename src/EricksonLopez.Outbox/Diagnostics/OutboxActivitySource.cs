// Copyright © Erickson Lopez. MIT License.
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Outbox.Diagnostics;

/// <summary>
/// Provides strongly-typed OpenTelemetry tracing capabilities for the Outbox pattern, propagating W3C
/// TraceContext across the transaction-boundary gap between message storage and broker dispatch.
/// </summary>
/// <remarks>
/// Links producer spans across time boundaries (database write → dispatcher publish) to guarantee
/// end-to-end observability in distributed systems.
/// Follows the OpenTelemetry Messaging Semantic Conventions:
/// https://opentelemetry.io/docs/specs/semconv/messaging/
/// </remarks>
public static class OutboxActivitySource
{
    /// <summary>The name of the ActivitySource used for distributed tracing.</summary>
    public const string SourceName = "EricksonLopez.Outbox";

    /// <summary>The <see cref="ActivitySource"/> instance used by the Outbox.</summary>
    public static readonly ActivitySource Source = new(SourceName, "2.0.0");

    /// <summary>
    /// The <c>messaging.system</c> value used when the actual broker name is not known.
    /// Store activities always use this value. Dispatch activities should use the actual
    /// broker name (<c>"rabbitmq"</c>, <c>"kafka"</c>, etc.) via the <c>brokerSystemName</c>
    /// parameter of <see cref="StartDispatchActivity"/>.
    /// </summary>
    public const string OutboxSystemName = "outbox";

    /// <summary>
    /// Starts a tracing activity when a message is dispatched to the network.
    /// This should link back to the TraceId originally saved in the database metadata.
    ///
    /// <para>
    /// Emitted tags follow the OpenTelemetry Messaging Semantic Conventions (v1.26+):
    /// <list type="bullet">
    ///   <item><description>
    ///   <c>messaging.system</c> — identifies the messaging broker.
    ///   Defaults to <c>"outbox"</c> when no broker-specific name is provided.
    ///   The dispatcher automatically reads <see cref="IBrokerPublisher.BrokerSystemName"/>
    ///   from the registered broker publisher and passes it here, so this tag is populated
    ///   with the actual broker name (e.g., <c>"rabbitmq"</c>, <c>"kafka"</c>) when the
    ///   broker publisher overrides the property.
    ///   </description></item>
    ///   <item><description><c>messaging.operation.name</c> — always "publish" for the producer side.</description></item>
    ///   <item><description><c>messaging.operation.type</c> — always "publish" for the producer side (OTel semconv 2024 structured enum, parallel to <c>messaging.operation.name</c>).</description></item>
    ///   <item><description><c>messaging.destination.name</c> — the message type alias (routing key).</description></item>
    ///   <item><description><c>messaging.message.id</c> — the unique outbox message ID.</description></item>
    ///   <item><description><c>messaging.message.conversation_id</c> — the correlation ID for tracing across services.</description></item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="messageType">Message type alias used as the destination name.</param>
    /// <param name="correlationId">Optional correlation ID (W3C conversation ID).</param>
    /// <param name="parentTraceId">Optional W3C traceparent header from message headers.</param>
    /// <param name="parentTraceState">Optional W3C tracestate header from message headers.</param>
    /// <param name="messageId">Optional unique outbox message ID.</param>
    /// <param name="brokerSystemName">
    /// The broker-specific <c>messaging.system</c> value (e.g., "rabbitmq", "kafka", "aws_sqs").
    /// When <c>null</c>, defaults to <c>"outbox"</c>. Broker publishers should supply this
    /// to comply with OTel Messaging semantic conventions. See <see cref="OutboxSystemName"/>.
    /// </param>
    public static Activity? StartDispatchActivity(
        string messageType,
        string? correlationId,
        string? parentTraceId,
        string? parentTraceState = null,
        string? messageId = null,
        string? brokerSystemName = null)
    {
        ActivityContext parentContext = default;
        if (!string.IsNullOrEmpty(parentTraceId) && ActivityContext.TryParse(parentTraceId, parentTraceState, out var parsedContext))
        {
            parentContext = parsedContext;
        }

        // ActivityKind.Producer signifies we are pushing a message out to a broker
        var activity = Source.StartActivity("Outbox.Dispatch", ActivityKind.Producer, parentContext);

        if (activity is null) return null;

        // P0-FIX: OTel Messaging Semantic Convention: messaging.system
        // OTel spec defines this as the name of the actual messaging broker, NOT the
        // name of the library or pattern. For example: "rabbitmq", "kafka", "aws_sqs".
        // When no broker name is provided (e.g., the core dispatcher doesn't know the broker),
        // fall back to "outbox". Broker publishers SHOULD override this by calling
        // Activity.Current?.SetTag("messaging.system", "rabbitmq") inside their PublishRawAsync,
        // or by passing brokerSystemName into it from their broker-specific call site.
        activity.SetTag("messaging.system", brokerSystemName ?? OutboxSystemName);

        // messaging.operation.name: "publish" for producer-side spans (we are sending a message).
        activity.SetTag("messaging.operation.name", "publish");
        // P3-A FIX: messaging.operation.type — structured enum introduced in OTel Messaging semconv 2024.
        // Parallel to messaging.operation.name; set both for compatibility with OTel collectors
        // that have migrated to the typed field and those that still use the string field.
        // Valid values for producer spans: "publish" | "create" | "receive" | "settle".
        activity.SetTag("messaging.operation.type", "publish");

        // messaging.destination.name: The destination where the message is published.
        // For outbox, this is the message type alias (e.g., "order.created.v1").
        activity.SetTag("messaging.destination.name", messageType);

        // messaging.message.id: Unique identifier of the message.
        if (messageId != null)
        {
            activity.SetTag("messaging.message.id", messageId);
        }

        // messaging.message.conversation_id: The conversation ID (replaces proprietary outbox.correlation_id).
        // This is the W3C-standard tag for correlation IDs in messaging contexts.
        if (correlationId != null)
        {
            activity.SetTag("messaging.message.conversation_id", correlationId);
        }

        return activity;
    }

    /// <summary>
    /// Starts a tracing activity when a message is first stored in the outbox (producer "create" operation).
    ///
    /// <para>
    /// This activity completes the end-to-end trace chain:
    /// <list type="bullet">
    ///   <item><description>Business transaction → <b>outbox.create</b> (the store operation)</description></item>
    ///   <item><description>Dispatcher reads message → <see cref="StartDispatchActivity"/> (outbox.publish)</description></item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// OTel Messaging Semantic Convention operation type: <c>"create"</c><br/>
    /// Provides the producer side when a message is first produced but not yet transmitted.
    /// Reference: https://opentelemetry.io/docs/specs/semconv/messaging/messaging-spans/#operation-types
    /// </para>
    ///
    /// <para>
    /// The <c>messaging.system</c> tag is set to <c>"outbox"</c> for store activities because
    /// the store operation is logically part of the outbox infrastructure, not the broker.
    /// The dispatch activity uses the actual broker name.
    /// </para>
    /// </summary>
    /// <param name="messageType">The message type alias (e.g., "order.created.v1").</param>
    /// <param name="messageId">The Guid of the outbox message, for correlation.</param>
    /// <param name="correlationId">Optional W3C conversation ID for distributed tracing.</param>
    /// <returns>The started activity, or <c>null</c> if no listeners are registered.</returns>
    public static Activity? StartStoreActivity(
        string messageType,
        string messageId,
        string? correlationId = null)
    {
        // ActivityKind.Producer: we are producing a message for asynchronous downstream consumption.
        var activity = Source.StartActivity("Outbox.Store", ActivityKind.Producer);

        if (activity is null) return null;

        // Store activities always use "outbox" — the store is a library concern, not a broker concern.
        // The dispatch activity is where the actual broker name (e.g., "rabbitmq") matters.
        activity.SetTag("messaging.system", OutboxSystemName);
        // OTel semconv: "create" = producer stores message before transmission (not yet published)
        activity.SetTag("messaging.operation.name", "create");
        // P3-A FIX: messaging.operation.type (OTel semconv 2024) — set to "create" for store activities.
        activity.SetTag("messaging.operation.type", "create");
        activity.SetTag("messaging.destination.name", messageType);
        activity.SetTag("messaging.message.id", messageId);

        if (correlationId != null)
        {
            activity.SetTag("messaging.message.conversation_id", correlationId);
        }

        return activity;
    }
}



