using System;
using System.Collections.Generic;
using AwesomeAssertions;
using EricksonLopez.Outbox.Serialization;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

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
    }

    [Fact]
    public void Constructor_Should_Throw_On_Empty_Alias()
    {
        var mappings = new List<(string, Type)>
        {
            ("", typeof(TestType))
        };

        Action act = () => _ = new InMemoryMessageTypeResolver(mappings);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetAlias_Should_Throw_On_Unregistered_Type()
    {
        var resolver = new InMemoryMessageTypeResolver(new List<(string, Type)>());

        Action act = () => resolver.GetAlias(typeof(TestType));
        act.Should().Throw<InvalidOperationException>();
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

    public class TestType { }
}


