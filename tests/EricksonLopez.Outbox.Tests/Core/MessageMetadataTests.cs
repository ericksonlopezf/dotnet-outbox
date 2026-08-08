using System;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Core;

public class MessageMetadataTests
{
    [Fact]
    public void Default_Struct_Should_Handle_GetValue_Gracefully()
    {
        var sut = default(MessageMetadata);
        
        var value = sut.GetValue("AnyKey");
        
        value.Should().BeNull();
        sut.Entries.Length.Should().Be(0);
    }
}


