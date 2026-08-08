using System;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

public class PublisherTests
{
    [Fact]
    public void Create_Should_Throw_When_Name_Is_Null_Or_Empty()
    {
        Action act1 = () => Publisher.Create(null!);
        act1.Should().Throw<ArgumentException>();

        Action act2 = () => Publisher.Create("");
        act2.Should().Throw<ArgumentException>();

        Action act3 = () => Publisher.Create("   ");
        act3.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_Should_Set_Properties()
    {
        var publisher = Publisher.Create("TestApp");

        publisher.Id.Should().NotBeNullOrEmpty();
        publisher.Name.Should().Be("TestApp");
        publisher.RegisteredAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }
}


