using System;
using EricksonLopez.Outbox.Contracts;

namespace EricksonLopez.Outbox.Benchmarks;

[OutboxMessage("order.created.v1")]
public sealed record OrderCreatedEvent(Guid OrderId, decimal Total, DateTimeOffset OccurredOn);

[OutboxMessage("order.confirmed.v1")]
public sealed record OrderConfirmedEvent(Guid OrderId, DateTimeOffset ConfirmedAt);

[OutboxMessage("variable.payload.v1")]
public sealed record VariablePayloadEvent(Guid Id, string Data);

/// <summary>
/// JSON context for benchmarks — manually declared because the [OutboxMessage] source generator
/// cannot emit a partial JsonSerializerContext that STJ's generator completes cross-generator.
/// </summary>
[System.Text.Json.Serialization.JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
[System.Text.Json.Serialization.JsonSerializable(typeof(OrderCreatedEvent))]
[System.Text.Json.Serialization.JsonSerializable(typeof(OrderConfirmedEvent))]
[System.Text.Json.Serialization.JsonSerializable(typeof(VariablePayloadEvent))]
public partial class BenchmarkJsonContext : System.Text.Json.Serialization.JsonSerializerContext { }
