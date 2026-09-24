// Copyright © Erickson Lopez. MIT License.
using System.Text.Json.Serialization;

namespace EricksonLopez.Outbox.AotSmokeTest;

[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TestMessage))]
public partial class OutboxGeneratedJsonContext : JsonSerializerContext
{
}
