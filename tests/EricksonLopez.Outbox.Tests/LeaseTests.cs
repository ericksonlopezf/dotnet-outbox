using System;
using EricksonLopez.Outbox;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

public class LeaseTests
{
    [Fact]
    public void IsExpired_WhenNowIsPastExpiresAt_ReturnsTrue()
    {
        // Arrange
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var lease = new Lease("resource-1", "owner-1", expiresAt);

        // Act
        var result = lease.IsExpired(DateTimeOffset.UtcNow);

        // Assert
        Assert.True(result);
    }
    
    [Fact]
    public void IsExpired_WhenNowIsBeforeExpiresAt_ReturnsFalse()
    {
        // Arrange
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        var lease = new Lease("resource-1", "owner-1", expiresAt);

        // Act
        var result = lease.IsExpired(DateTimeOffset.UtcNow);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsExpired_WhenNowIsExactlyExpiresAt_ReturnsTrue()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var lease = new Lease("resource-1", "owner-1", now);

        // Act
        var result = lease.IsExpired(now);

        // Assert
        Assert.True(result);
    }
}


