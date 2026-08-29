// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using EricksonLopez.Result;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Delivery;

public class DispatchResultTests
{
    [Fact]
    public void Ok_CreatesSuccessfulResult()
    {
        var result = DispatchResult.Ok();

        result.Success.Should().BeTrue();
        result.ShouldRetry.Should().BeFalse();
        result.Error.Should().BeNull();
        result.IncrementRetryCount.Should().BeFalse();
    }

    [Fact]
    public void FailAndRetry_CreatesTransientFailure()
    {
        var ex = new InvalidOperationException("test");
        var result = DispatchResult.FailAndRetry(ex);

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
        result.Error.Should().Be(ex);
        result.IncrementRetryCount.Should().BeTrue();

        Assert.Throws<ArgumentNullException>(() => DispatchResult.FailAndRetry(null!));
    }

    [Fact]
    public void FailAndRetry_WithIncrementFlag_CreatesTransientFailure()
    {
        var ex = new InvalidOperationException("test");
        var result = DispatchResult.FailAndRetry(ex, false);

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
        result.Error.Should().Be(ex);
        result.IncrementRetryCount.Should().BeFalse();

        Assert.Throws<ArgumentNullException>(() => DispatchResult.FailAndRetry(null!, false));
    }

    [Fact]
    public void FailFatal_CreatesFatalFailure()
    {
        var ex = new InvalidOperationException("test");
        var result = DispatchResult.FailFatal(ex);

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeFalse();
        result.Error.Should().Be(ex);
        result.IncrementRetryCount.Should().BeFalse();

        Assert.Throws<ArgumentNullException>(() => DispatchResult.FailFatal((Exception)null!));
    }

    [Fact]
    public void FailFatal_WithMessage_CreatesFatalFailure()
    {
        var result = DispatchResult.FailFatal("reason");

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error.Should().BeOfType<OutboxDispatchException>();
        result.IncrementRetryCount.Should().BeFalse();

        Assert.Throws<ArgumentNullException>(() => DispatchResult.FailFatal((string)null!));
    }

    [Fact]
    public void FailFatal_WithDetails_CreatesFatalFailure()
    {
        var id = Guid.NewGuid();
        var result = DispatchResult.FailFatal(id, 2, "reason");

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeFalse();
        result.Error.Should().NotBeNull();
        
        var ex = result.Error as OutboxDispatchException;
        ex.Should().NotBeNull();
        ex!.MessageId.Should().Be(id);
        ex.AttemptCount.Should().Be(2);

        Assert.Throws<ArgumentNullException>(() => DispatchResult.FailFatal(id, 2, null!));
    }

    [Fact]
    public void ThrowIfInvalid_WhenValid_DoesNothing()
    {
        DispatchResult.Ok().ThrowIfInvalid();
        DispatchResult.FailAndRetry(new InvalidOperationException()).ThrowIfInvalid();
        DispatchResult.FailFatal(new InvalidOperationException()).ThrowIfInvalid();
    }

    [Fact]
    public void ThrowIfInvalid_WhenSuccessAndShouldRetry_Throws()
    {
        var result = new DispatchResult(true, true, null, false);
        var ex = Assert.Throws<InvalidOperationException>(() => result.ThrowIfInvalid());
        ex.Message.Should().Be("DispatchResult is in an invalid state: Success=true and ShouldRetry=true are mutually exclusive. A successful dispatch should never request a retry. Use DispatchResult.Ok() or DispatchResult.FailAndRetry().");
    }

    [Fact]
    public void ThrowIfInvalid_WhenFailedButNoError_Throws()
    {
        var result = new DispatchResult(false, false, null, false);
        var ex = Assert.Throws<InvalidOperationException>(() => result.ThrowIfInvalid());
        ex.Message.Should().Be("Failed DispatchResult must have an Error attached to it.");
    }
}

