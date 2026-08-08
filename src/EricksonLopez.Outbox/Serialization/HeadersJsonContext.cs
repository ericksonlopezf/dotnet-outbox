using System.Collections.Generic;
using System.Text.Json.Serialization;

using System.Diagnostics.CodeAnalysis;

namespace EricksonLopez.Outbox.Serialization;

[ExcludeFromCodeCoverage]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class HeadersJsonContext : JsonSerializerContext
{
}
