// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Routing;

public class PublisherTests
{
    [Fact]
    public void Create_Should_Throw_When_Name_Is_Null_Or_Empty()
    {
        Action act1 = () => Publisher.Create(null!);
        var ex1 = act1.Should().Throw<ArgumentException>();
        ex1.WithMessage("Publisher name cannot be null or empty. (Parameter 'name')");

        Action act2 = () => Publisher.Create("");
        var ex2 = act2.Should().Throw<ArgumentException>();
        ex2.WithMessage("Publisher name cannot be null or empty. (Parameter 'name')");

        Action act3 = () => Publisher.Create("   ");
        var ex3 = act3.Should().Throw<ArgumentException>();
        ex3.WithMessage("Publisher name cannot be null or empty. (Parameter 'name')");
    }

    [Fact]
    public void Create_Should_Set_Properties_With_Exact_Format()
    {
        var publisher = Publisher.Create("TestApp");

        publisher.Id.Length.Should().Be(32);
        Guid.TryParseExact(publisher.Id, "N", out _).Should().BeTrue();
        publisher.Name.Should().Be("TestApp");
        publisher.RegisteredAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void None_Should_Have_Expected_Constants()
    {
        Publisher.None.Id.Should().Be("00000000000000000000000000000000");
        Publisher.None.Name.Should().Be("none");
        Publisher.None.RegisteredAt.Should().Be(DateTimeOffset.MinValue);
    }
}
