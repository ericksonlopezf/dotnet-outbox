// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace EricksonLopez.Outbox.Serialization;


[ExcludeFromCodeCoverage]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class HeadersJsonContext : JsonSerializerContext
{
}

