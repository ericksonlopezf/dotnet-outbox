// Copyright © Erickson Lopez. MIT License.
using System.Text.Json.Serialization;

namespace EricksonLopez.Outbox.Benchmarks;

/// <summary>
/// JSON context for benchmarks — manually declared because the [OutboxMessage] source generator
/// cannot emit a partial JsonSerializerContext that STJ's generator completes cross-generator.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(OrderCreatedEvent))]
[JsonSerializable(typeof(OrderConfirmedEvent))]
[JsonSerializable(typeof(VariablePayloadEvent))]
public partial class BenchmarkJsonContext : JsonSerializerContext { }
