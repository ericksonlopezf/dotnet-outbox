// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Delivery;

public class OutboxTests
{
    [Fact]
    public async Task StoreAsync_SingleMessage_ShouldCallUnderlyingStoreMethod()
    {
        // Arrange
        var outboxMock = Substitute.For<IOutbox>();
        var message = new { Id = Guid.NewGuid(), Data = "Test" };
        var transaction = Substitute.For<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>();
        var cancellationToken = CancellationToken.None;

        // Act
        await outboxMock.StoreAsync(message, transaction, cancellationToken);

        // Assert
        await outboxMock.Received(1).StoreAsync(message, transaction, cancellationToken);
    }
    
    [Fact]
    public async Task StoreAsync_BatchMessages_ShouldCallUnderlyingStoreMethod()
    {
        // Arrange
        var outboxMock = Substitute.For<IOutbox>();
        var messages = new List<object> 
        { 
            new { Id = Guid.NewGuid(), Data = "Item 1" }, 
            new { Id = Guid.NewGuid(), Data = "Item 2" } 
        };
        var transaction = Substitute.For<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>();
        var cancellationToken = CancellationToken.None;

        // Act
        await outboxMock.StoreAsync(messages, transaction, cancellationToken);

        // Assert
        await outboxMock.Received(1).StoreAsync(messages, transaction, cancellationToken);
    }
}





