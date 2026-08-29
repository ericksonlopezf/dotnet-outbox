// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using AwesomeAssertions;
using EricksonLopez.Outbox.Serialization;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Delivery;

public class TypeResolverTests
{
    [Fact]
    public void Constructor_Should_Initialize_Mappings()
    {
        var mappings = new List<(string, Type)>
        {
            ("test.alias", typeof(TestType))
        };

        var resolver = new InMemoryMessageTypeResolver(mappings);

        resolver.GetAlias(typeof(TestType)).Should().Be("test.alias");
        resolver.Resolve("test.alias").Should().Be<TestType>();
        resolver.GetAllMappings().Should().ContainKey("test.alias");
        resolver.GetAllMappings()["test.alias"].Should().Be<TestType>();
    }

    [Fact]
    public void Resolve_Should_Be_Case_Insensitive()
    {
        var mappings = new List<(string, Type)>
        {
            ("Test.Alias", typeof(TestType))
        };

        var resolver = new InMemoryMessageTypeResolver(mappings);

        resolver.Resolve("test.alias").Should().Be<TestType>();
        resolver.Resolve("TEST.ALIAS").Should().Be<TestType>();
        resolver.Resolve("Test.Alias").Should().Be<TestType>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_Should_Throw_On_NullOrWhitespace_Alias(string? alias)
    {
        var mappings = new List<(string, Type)>
        {
            (alias!, typeof(TestType))
        };

        Action act = () => _ = new InMemoryMessageTypeResolver(mappings);
        act.Should().Throw<ArgumentException>().WithMessage($"Alias for type {nameof(TestType)} cannot be null or empty.");
    }

    [Fact]
    public void GetAlias_Should_Throw_On_Unregistered_Type()
    {
        var resolver = new InMemoryMessageTypeResolver(new List<(string, Type)>());

        Action act = () => resolver.GetAlias(typeof(TestType));
        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"Type '{typeof(TestType).FullName}' is not registered in the OutboxMessageTypeResolver. Decorate it with [OutboxMessage(\"your.alias\")] and register it during startup.");
    }

    [Fact]
    public void TryGetAlias_Should_Return_False_When_Unregistered()
    {
        var resolver = new InMemoryMessageTypeResolver(new List<(string, Type)>());

        resolver.TryGetAlias(typeof(TestType), out var alias).Should().BeFalse();
        alias.Should().BeNull();
    }

    [Fact]
    public void Resolve_Should_Return_Null_On_Unregistered_Alias()
    {
        var resolver = new InMemoryMessageTypeResolver(new List<(string, Type)>());

        resolver.Resolve("unknown").Should().BeNull();
    }
    
    [Fact]
    public void Generic_Interface_Methods_Should_Delegate_To_Type_Methods()
    {
        var resolver = new InMemoryMessageTypeResolver(new List<(string, Type)> { ("test", typeof(TestType)) });
        IOutboxMessageTypeResolver interfaceResolver = resolver;

        interfaceResolver.TryGetAlias<TestType>(out var alias).Should().BeTrue();
        alias.Should().Be("test");
        
        interfaceResolver.GetAlias<TestType>().Should().Be("test");
    }

    [Fact]
    public void Generic_Interface_Methods_Should_Work_When_Not_Found()
    {
        var resolver = new InMemoryMessageTypeResolver(new List<(string, Type)>());
        IOutboxMessageTypeResolver interfaceResolver = resolver;

        interfaceResolver.TryGetAlias<TestType>(out var alias).Should().BeFalse();
        alias.Should().BeNull();

        Action act = () => interfaceResolver.GetAlias<TestType>();
        act.Should().Throw<InvalidOperationException>();
    }

    public class TestType { }
}
