// Copyright © Erickson Lopez. MIT License.
using System;
using System.Buffers;
using System.Linq;
using AwesomeAssertions;
using EricksonLopez.Outbox.Serialization;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Serialization;

public class IOutboxSerializerTests
{
    private sealed class DummySerializer : IOutboxSerializer
    {
        public ReadOnlyMemory<byte> Serialize<TMessage>(TMessage message)
        {
            return new byte[] { 1, 2, 3 };
        }

        public TMessage Deserialize<TMessage>(ReadOnlySpan<byte> data) => default!;
    }

    [Fact]
    public void Default_Serialize_Should_Delegate_To_Serialize()
    {
        IOutboxSerializer sut = new DummySerializer();
        var buffer = new ArrayBufferWriter<byte>();
        
        sut.Serialize("test", buffer);

        buffer.WrittenSpan.ToArray().Should().BeEquivalentTo(new byte[] { 1, 2, 3 });
    }
}




