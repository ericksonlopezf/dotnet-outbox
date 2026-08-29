// Copyright © Erickson Lopez. MIT License.
using System;
using System.Runtime.InteropServices;

namespace EricksonLopez.Outbox.Storage.PostgreSql;

internal static class PostgreSqlMemoryExtensions
{
    public static byte[] ToByteArray(this ReadOnlyMemory<byte> memory)
    {
        if (MemoryMarshal.TryGetArray(memory, out var seg))
        {
            if (seg.Offset == 0)
            {
                if (seg.Array != null && seg.Count == seg.Array.Length)
                {
                    return seg.Array;
                }
            }
        }

        return memory.ToArray();
    }
}
