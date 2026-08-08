using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Outbox;

/// <summary>
/// Defines a broker-agnostic contract for publishing messages to a message broker.
/// </summary>
/// <remarks>
/// <para>
/// All broker adapters (RabbitMQ, Kafka, Azure Service Bus, etc.) implement this interface.
/// </para>
/// <para>
/// <b>Design rationale:</b><br/>
///   - The generic overloads are intended for strongly-typed publishing at the application boundary.<br/>
///   - The raw overload is used by the dispatcher, operating on already-serialized <see cref="OutboxMessage"/>
///     without needing to reconstruct generics at dispatch time, ensuring NativeAOT compatibility.
/// </para>
/// <para>
/// <b>Dispatcher usage:</b> The core dispatcher exclusively uses
/// <see cref="PublishRawAsync"/> (per-message path). <see cref="ITypedBrokerPublisher.PublishBatchAsync{T}"/> is
/// provided for use in application code (e.g., event fanning-out within a single transaction).
/// </para>
/// </remarks>
public interface IBrokerPublisher
{


    /// <summary>
    /// Publishes a raw serialized outbox message as-is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Used by the Dispatcher which works with pre-serialized payloads, avoiding
    /// runtime generics and reflection for NativeAOT compatibility.
    /// </para>
    /// <para>
    /// <b>DispatchResult contract for implementers:</b>
    /// </para>
    /// <list type="table">
    ///   <listheader>
    ///     <term>Scenario</term>
    ///     <term>Return value</term>
    ///   </listheader>
    ///   <item>
    ///     <term>Message published successfully</term>
    ///     <term><c>DispatchResult.Ok()</c></term>
    ///   </item>
    ///   <item>
    ///     <term>Transient / recoverable failure (network timeout, broker unreachable, rate-limited)</term>
    ///     <term><c>DispatchResult.FailAndRetry(exception)</c></term>
    ///   </item>
    ///   <item>
    ///     <term>Fatal / unrecoverable failure (schema mismatch, serialization error, message too large for broker, authorization denied)</term>
    ///     <term><c>DispatchResult.FailFatal(exception)</c></term>
    ///   </item>
    /// </list>
    /// <para>
    /// <b>Do NOT throw exceptions</b> here. Thrown exceptions are treated as
    /// fatal failures by the dispatcher (equivalent to <c>DispatchResult.FailFatal</c>).
    /// Always catch broker exceptions and map them to the appropriate <see cref="DispatchResult"/>.
    /// </para>
    /// <para>
    /// <b>Do NOT return <c>default(DispatchResult)</c></b>. The default value of the struct
    /// (<c>Success=false, ShouldRetry=false, Error=null</c>) is an invalid state that will
    /// cause the dispatcher to dead-letter the message with a misleading "no error" state.
    /// Always use one of the factory methods: <c>DispatchResult.Ok()</c>,
    /// <c>DispatchResult.FailAndRetry(ex)</c>, or <c>DispatchResult.FailFatal(ex)</c>.
    /// </para>
    /// <para>
    /// The dispatcher wraps calls to the publisher with a retry interceptor,
    /// which automatically retries <c>FailAndRetry</c> results with exponential backoff.
    /// Returning <c>FailFatal</c> bypasses retry and dead-letters the message immediately.
    /// </para>
    /// <para>
    /// <b>OpenTelemetry — <c>messaging.system</c> tag requirement:</b><br/>
    /// The outbox core sets <c>messaging.system = "outbox"</c> as a default on the dispatch Activity.
    /// Broker implementers <b>MUST</b> override this tag with the actual broker name:
    /// <code>
    /// System.Diagnostics.Activity.Current?.SetTag("messaging.system", "rabbitmq"); // or "kafka", "azure_service_bus", etc.
    /// </code>
    /// This is required by the OpenTelemetry Messaging Semantic Conventions (v1.26+):
    /// https://opentelemetry.io/docs/specs/semconv/messaging/messaging-spans/#messaging-attributes
    /// </para>
    /// </remarks>
    /// <param name="message">The raw outbox message to publish.</param>
    /// <param name="metadata">The metadata associated with the message.</param>
    /// <param name="context">The context governing the dispatch operation, including cancellation tokens.</param>
    /// <returns>A task that represents the asynchronous dispatch operation, containing the <see cref="DispatchResult"/>.</returns>
    ValueTask<DispatchResult> PublishRawAsync(
        OutboxMessage message,
        MessageMetadata metadata,
        DispatchContext context);

    /// <summary>
    /// Gets the OpenTelemetry <c>messaging.system</c> attribute value for this broker implementation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The outbox dispatcher reads this value to set the <c>messaging.system</c> tag on the dispatch
    /// <see cref="System.Diagnostics.Activity"/>, conforming to the OpenTelemetry Messaging Semantic
    /// Conventions (v1.26+). See: https://opentelemetry.io/docs/specs/semconv/messaging/messaging-spans/
    /// </para>
    /// <para>
    /// <b>Override this property in your broker publisher</b> to return the canonical OTel system name:
    /// <list type="table">
    ///   <listheader><term>Broker</term><term>Return value</term></listheader>
    ///   <item><term>RabbitMQ</term><term><c>"rabbitmq"</c></term></item>
    ///   <item><term>Apache Kafka</term><term><c>"kafka"</c></term></item>
    ///   <item><term>Azure Service Bus</term><term><c>"azure_service_bus"</c></term></item>
    ///   <item><term>Amazon SQS</term><term><c>"aws_sqs"</c></term></item>
    ///   <item><term>Google Pub/Sub</term><term><c>"gcp_pubsub"</c></term></item>
    ///   <item><term>NATS</term><term><c>"nats"</c></term></item>
    ///   <item><term>Redis Streams</term><term><c>"redis"</c></term></item>
    /// </list>
    /// </para>
    /// <para>
    /// The default implementation returns <c>"outbox"</c> as a fallback sentinel value.
    /// The dispatcher will use this value automatically — broker publishers no longer need to
    /// call <c>Activity.Current?.SetTag("messaging.system", ...)</c> manually.
    /// </para>
    /// </remarks>
    string BrokerSystemName => Diagnostics.OutboxActivitySource.OutboxSystemName;
}

/// <summary>
/// Extends <see cref="IBrokerPublisher"/> to support strongly-typed publishing.
/// </summary>
public interface ITypedBrokerPublisher : IBrokerPublisher
{
    /// <summary>
    /// Publishes a strongly-typed message envelope to the underlying broker.
    /// </summary>
    ValueTask<DispatchResult> PublishAsync<T>(
        MessageEnvelope<T> message,
        DispatchContext context) where T : notnull;

    /// <summary>
    /// Publishes a batch of strongly-typed message envelopes to the underlying broker.
    /// </summary>
    ValueTask<IReadOnlyList<DispatchResult>> PublishBatchAsync<T>(
        IReadOnlyList<MessageEnvelope<T>> messages,
        DispatchContext context) where T : notnull;
}
