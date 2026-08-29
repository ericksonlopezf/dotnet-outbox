// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Delivery;

public class MessageMetadataTests
{
    [Fact]
    public void Default_Struct_Should_Handle_GetValue_Gracefully()
    {
        var sut = default(OutboxMessageMetadata);
        
        var value = sut.GetValue("AnyKey");
        
        value.Should().BeNull();
        sut.Entries.Length.Should().Be(0);
    }
}




