using System;
using System.Collections.Generic;
using AwesomeAssertions;
using NSubstitute;
using Xunit;
using EricksonLopez.Outbox.Dispatcher;

namespace EricksonLopez.Outbox.Tests.Dispatcher;

public class DefaultBrokerSelectorTests
{
    [Fact]
    public void GetPublisher_RouteExists_ReturnsRoutedPublisher()
    {
        var defaultPublisher = Substitute.For<IBrokerPublisher>();
        var routedPublisher = Substitute.For<IBrokerPublisher>();
        var routes = new Dictionary<string, IBrokerPublisher> { { "TestMessage", routedPublisher } };
        
        var sut = new DefaultBrokerSelector(defaultPublisher, routes);
        var message = new OutboxMessage(Guid.NewGuid(), "TestMessage", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        
        var result = sut.GetPublisher(message);
        
        result.Should().BeSameAs(routedPublisher);
    }

    [Fact]
    public void GetPublisher_RouteDoesNotExist_HasDefault_ReturnsDefaultPublisher()
    {
        var defaultPublisher = Substitute.For<IBrokerPublisher>();
        var routes = new Dictionary<string, IBrokerPublisher>();
        
        var sut = new DefaultBrokerSelector(defaultPublisher, routes);
        var message = new OutboxMessage(Guid.NewGuid(), "UnknownMessage", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        
        var result = sut.GetPublisher(message);
        
        result.Should().BeSameAs(defaultPublisher);
    }

    [Fact]
    public void GetPublisher_RouteDoesNotExist_NoDefault_ThrowsInvalidOperationException()
    {
        var sut = new DefaultBrokerSelector(null);
        var message = new OutboxMessage(Guid.NewGuid(), "UnknownMessage", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        
        Action act = () => sut.GetPublisher(message);
        
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No broker publisher configured for message type 'UnknownMessage'*");
    }

    [Fact]
    public void Constructor_NullRoutes_InitializesEmptyRoutes()
    {
        var sut = new DefaultBrokerSelector(null, null);
        var message = new OutboxMessage(Guid.NewGuid(), "UnknownMessage", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        
        Action act = () => sut.GetPublisher(message);
        
        act.Should().Throw<InvalidOperationException>();
    }
}
