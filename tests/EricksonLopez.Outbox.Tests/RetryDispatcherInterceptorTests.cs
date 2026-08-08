#pragma warning disable CA2012
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Retry;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

public class RetryDispatcherInterceptorTests
{
    [Fact]
    public async Task PublishAsync_Should_Succeed_On_First_Try()
    {
        var inner = Substitute.For<IBrokerPublisher>();
        var policy = new ExponentialBackoffRetryPolicy(TimeSpan.FromMilliseconds(10), 3);
        var interceptor = new RetryDispatcherInterceptor(inner, policy, new CircuitBreakerState(), NullLogger<RetryDispatcherInterceptor>.Instance);

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", default, null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new MessageMetadata(null, null, null);
        var ctx = new DispatchContext(default, 1);

        _ = inner.PublishRawAsync(msg, meta, Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.Ok()));

        var result = await interceptor.PublishRawAsync(msg, meta, ctx);

        result.Success.Should().BeTrue();
        await inner.Received(1).PublishRawAsync(msg, meta, Arg.Any<DispatchContext>());
    }

    [Fact]
    public async Task PublishAsync_Should_Retry_On_Failure()
    {
        var inner = Substitute.For<IBrokerPublisher>();
        var policy = new ExponentialBackoffRetryPolicy(TimeSpan.FromMilliseconds(10), 3);
        var interceptor = new RetryDispatcherInterceptor(inner, policy, new CircuitBreakerState(), NullLogger<RetryDispatcherInterceptor>.Instance);

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", default, null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new MessageMetadata(null, null, null);
        var ctx = new DispatchContext(default, 1);

        _ = inner.PublishRawAsync(msg, meta, Arg.Any<DispatchContext>())
            .Returns(
                new ValueTask<DispatchResult>(DispatchResult.FailAndRetry(new InvalidOperationException())),
                new ValueTask<DispatchResult>(DispatchResult.Ok()));

        var result = await interceptor.PublishRawAsync(msg, meta, ctx);

        result.Success.Should().BeTrue();
        await inner.Received(2).PublishRawAsync(msg, meta, Arg.Any<DispatchContext>());
    }

    [Fact]
    public async Task PublishAsync_Should_Not_Retry_Fatal_Errors()
    {
        var inner = Substitute.For<IBrokerPublisher>();
        var policy = new ExponentialBackoffRetryPolicy(TimeSpan.FromMilliseconds(10), 3);
        var interceptor = new RetryDispatcherInterceptor(inner, policy, new CircuitBreakerState(), NullLogger<RetryDispatcherInterceptor>.Instance);

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", default, null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new MessageMetadata(null, null, null);
        var ctx = new DispatchContext(default, 1);

        _ = inner.PublishRawAsync(msg, meta, Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.FailFatal(new InvalidOperationException())));

        var result = await interceptor.PublishRawAsync(msg, meta, ctx);

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeFalse();
        await inner.Received(1).PublishRawAsync(msg, meta, Arg.Any<DispatchContext>());
    }
    
    [Fact]
    public async Task PublishAsync_Should_Fail_After_Max_Attempts()
    {
        var inner = Substitute.For<IBrokerPublisher>();
        var policy = new ExponentialBackoffRetryPolicy(TimeSpan.FromMilliseconds(1), 2);
        var interceptor = new RetryDispatcherInterceptor(inner, policy, new CircuitBreakerState(), NullLogger<RetryDispatcherInterceptor>.Instance);

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", default, null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new MessageMetadata(null, null, null);
        var ctx = new DispatchContext(default, 1);

        var ex = new InvalidOperationException("Inner error");
        _ = inner.PublishRawAsync(msg, meta, Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.FailAndRetry(ex)));

        var result = await interceptor.PublishRawAsync(msg, meta, ctx);

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeFalse();
        result.Error.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be("Retry policy exhausted after 2 attempts.");
        result.Error!.InnerException.Should().Be(ex);
        
        await inner.Received(2).PublishRawAsync(msg, meta, Arg.Any<DispatchContext>());
    }

    [Fact]
    public async Task PublishRawAsync_Should_Succeed()
    {
        var inner = Substitute.For<IBrokerPublisher>();
        var policy = new ExponentialBackoffRetryPolicy(TimeSpan.FromMilliseconds(10), 3);
        var interceptor = new RetryDispatcherInterceptor(inner, policy, new CircuitBreakerState(), NullLogger<RetryDispatcherInterceptor>.Instance);

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", default, null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new MessageMetadata(null, null, null);
        var ctx = new DispatchContext(default, 1);

        _ = inner.PublishRawAsync(msg, meta, Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.Ok()));

        var result = await interceptor.PublishRawAsync(msg, meta, ctx);

        result.Success.Should().BeTrue();
        await inner.Received(1).PublishRawAsync(msg, meta, Arg.Any<DispatchContext>());
    }

    
    [Fact]
    public async Task PublishAsync_Should_ReturnFailAndRetry_NotFailFatal_WhenCancelled()
    {
        // ISSUE-ERR1 FIX VERIFICATION: Cancellation must return FailAndRetry (not FailFatal).
        // FailFatal would dead-letter the message during graceful shutdown / rolling deploy.
        // FailAndRetry keeps it in state=3 (Failed/Retry) for pickup on next startup.
        var inner = Substitute.For<IBrokerPublisher>();
        var policy = new ExponentialBackoffRetryPolicy(TimeSpan.FromSeconds(10), 3); // Long delay to ensure cancellation hits
        var interceptor = new RetryDispatcherInterceptor(inner, policy, new CircuitBreakerState(), NullLogger<RetryDispatcherInterceptor>.Instance);

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", default, null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new MessageMetadata(null, null, null);
        var cts = new CancellationTokenSource();
        var ctx = new DispatchContext(cts.Token, 1);

        _ = inner.PublishRawAsync(msg, meta, Arg.Any<DispatchContext>())
            .Returns(x => 
            {
                cts.Cancel(); // Cancel before next retry delay
                return new ValueTask<DispatchResult>(DispatchResult.FailAndRetry(new InvalidOperationException()));
            });

        var result = await interceptor.PublishRawAsync(msg, meta, ctx);

        result.Success.Should().BeFalse();
        // ISSUE-ERR1: Must be ShouldRetry=true (FailAndRetry), NOT ShouldRetry=false (FailFatal).
        // The message should be re-queued for retry, not permanently dead-lettered.
        result.ShouldRetry.Should().BeTrue();
        result.Error.Should().BeOfType<OperationCanceledException>();
    }

    [Fact]
    public async Task PublishAsync_CircuitBreakerOpen_Initially_ReturnsFail()
    {
        var inner = Substitute.For<IBrokerPublisher>();
        var policy = new EricksonLopez.Outbox.Retry.ExponentialBackoffRetryPolicy(TimeSpan.Zero, 0);
        
        var interceptor = new RetryDispatcherInterceptor(inner, policy, new EricksonLopez.Outbox.Retry.CircuitBreakerState(1, TimeSpan.FromHours(1)), NullLogger<RetryDispatcherInterceptor>.Instance);

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", default, null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new MessageMetadata(Guid.NewGuid().ToString(), null, null, Array.Empty<MetadataEntry>());
        var ctx = new DispatchContext(CancellationToken.None, 1);

        // Force open by failing once
        inner.PublishRawAsync(msg, meta, Arg.Any<DispatchContext>()).Returns(new ValueTask<DispatchResult>(DispatchResult.FailAndRetry(new InvalidOperationException())));
        

        await interceptor.PublishRawAsync(msg, meta, ctx); // Circuit opens here

        // Next call should be rejected immediately
        var result = await interceptor.PublishRawAsync(msg, meta, ctx);
        
        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
        result.Error.Should().BeOfType<EricksonLopez.Outbox.Retry.CircuitBreakerOpenException>()
            .Which.Message.Should().Be("Circuit breaker is open.");
        
        // Inner should not have been called a second time (only the first loop)
        _ = inner.Received(1).PublishRawAsync(msg, meta, Arg.Any<DispatchContext>());
    }

    [Fact]
    public async Task PublishAsync_CircuitBreakerOpen_DuringRetry_ReturnsFail()
    {
        var inner = Substitute.For<IBrokerPublisher>();
        var policy = new EricksonLopez.Outbox.Retry.ExponentialBackoffRetryPolicy(TimeSpan.Zero, 10);
        
        var interceptor = new RetryDispatcherInterceptor(inner, policy, new EricksonLopez.Outbox.Retry.CircuitBreakerState(2, TimeSpan.FromHours(1)), NullLogger<RetryDispatcherInterceptor>.Instance);

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", default, null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new MessageMetadata(Guid.NewGuid().ToString(), null, null, Array.Empty<MetadataEntry>());
        var ctx = new DispatchContext(CancellationToken.None, 1);

        // Fail first time (1 failure recorded)
        // Fail second time (2 failures recorded -> circuit opens during retry)
        int calls = 0; Console.WriteLine("Starting test");
        inner.PublishRawAsync(msg, meta, Arg.Any<DispatchContext>()).Returns(x => 
        {
            calls++; Console.WriteLine("Call " + calls);
            return new ValueTask<DispatchResult>(DispatchResult.FailAndRetry(new InvalidOperationException($"Call {calls}")));
        });

        

        var result = await interceptor.PublishRawAsync(msg, meta, ctx);
        
        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
        result.IncrementRetryCount.Should().BeFalse(); // From circuit breaker open
        result.Error.Should().BeOfType<EricksonLopez.Outbox.Retry.CircuitBreakerOpenException>()
            .Which.Message.Should().Be("Circuit breaker opened.");
        
        
        // It failed twice, then checked circuit breaker and aborted
        calls.Should().Be(2);
    }
}












