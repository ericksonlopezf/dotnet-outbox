// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;

namespace EricksonLopez.Outbox;

/// <summary>
/// Holds the per-consumer headers deserialization cache state.
/// Encapsulates stateful variables across asynchronous iterations.
/// </summary>
internal sealed class HeadersDeserializationCache
{
    public Dictionary<string, string> CurrentHeaders { get; private set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public ReadOnlyMemory<byte>? LastHeadersMemory { get; private set; }
    public Dictionary<string, string>? LastHeadersDict { get; private set; }

    /// <summary>Resets the cache at batch boundaries to prevent stale header references.</summary>
    public void Reset()
    {
        LastHeadersMemory = null;
        LastHeadersDict = null;
    }

    /// <summary>
    /// Swaps the current and last dictionaries after a successful parse.
    /// Avoids allocating a new dictionary on every unique header set.
    /// </summary>
    public void Swap(ReadOnlyMemory<byte> headersMemory, Dictionary<string, string> parsedHeaders)
    {
        LastHeadersMemory = headersMemory;
        var nextCurrent = LastHeadersDict ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        LastHeadersDict = parsedHeaders;
        CurrentHeaders = nextCurrent;
    }
}
