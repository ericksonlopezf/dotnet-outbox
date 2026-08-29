// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Routing;

public class IBrokerPublisherTests
{
    private sealed class DefaultBrokerPublisher : IBrokerPublisher
    {
        public ValueTask<DispatchResult> PublishRawAsync(OutboxMessage message, OutboxMessageMetadata metadata, DispatchContext context)
        {
            return new ValueTask<DispatchResult>(DispatchResult.Ok());
        }
    }

    [Fact]
    public void BrokerSystemName_HasDefaultImplementation()
    {
        IBrokerPublisher publisher = new DefaultBrokerPublisher();
        publisher.BrokerSystemName.Should().Be("outbox");
    }
}




