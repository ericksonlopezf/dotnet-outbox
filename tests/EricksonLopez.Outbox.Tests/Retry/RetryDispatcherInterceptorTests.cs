#pragma warning disable CA2012
using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using EricksonLopez.Outbox.Retry;

namespace EricksonLopez.Outbox.Tests.Retry;

public class RetryDispatcherInterceptorTests
{
    private readonly IBrokerPublisher _innerPublisher = Substitute.For<IBrokerPublisher>();
    private readonly ILogger<RetryDispatcherInterceptor> _logger = Substitute.For<ILogger<RetryDispatcherInterceptor>>();
    private readonly CircuitBreakerState _circuitBreaker = new();
    
    private sealed class EnabledLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
    
    private readonly ILogger<RetryDispatcherInterceptor> _enabledLogger = new EnabledLogger<RetryDispatcherInterceptor>();
    
    [Fact]
    public async Task PublishRawAsync_CircuitBreakerOpen_ReturnsFailAndRetryNoIncrement()
    {
        _circuitBreaker.RecordFailure();
        _circuitBreaker.RecordFailure();
        _circuitBreaker.RecordFailure(); // Assumes default thresholds open it

        // Force open
        var circuitBreaker = new CircuitBreakerState(1, TimeSpan.FromMinutes(1));
        circuitBreaker.RecordFailure();
        
        var sut = new RetryDispatcherInterceptor(_innerPublisher, new FixedDelayRetryPolicy(TimeSpan.Zero, 1), circuitBreaker, _enabledLogger);
        
        var message = new OutboxMessage(Guid.NewGuid(), "test", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        var metadata = new MessageMetadata();
        var context = new DispatchContext(CancellationToken.None, 1);
        
        var result = await sut.PublishRawAsync(message, metadata, context);
        
        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
        result.IncrementRetryCount.Should().BeFalse();
        result.Error.Should().BeOfType<CircuitBreakerOpenException>();
    }

    [Fact]
    public async Task PublishRawAsync_SuccessFirstTry_ReturnsSuccess()
    {
        var policy = new FixedDelayRetryPolicy(TimeSpan.Zero, 1);
        var sut = new RetryDispatcherInterceptor(_innerPublisher, policy, _circuitBreaker, _enabledLogger);
        
        var message = new OutboxMessage(Guid.NewGuid(), "test", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        var metadata = new MessageMetadata();
        var context = new DispatchContext(CancellationToken.None, 1);
        
        _ = _innerPublisher.PublishRawAsync(message, metadata, Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.Ok()));
            
        var result = await sut.PublishRawAsync(message, metadata, context);
        
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task PublishRawAsync_FailFatal_ReturnsFailFatal()
    {
        var policy = new FixedDelayRetryPolicy(TimeSpan.Zero, 1);
        var sut = new RetryDispatcherInterceptor(_innerPublisher, policy, _circuitBreaker, _enabledLogger);
        
        var message = new OutboxMessage(Guid.NewGuid(), "test", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        var metadata = new MessageMetadata();
        var context = new DispatchContext(CancellationToken.None, 1);
        
        var exception = new InvalidOperationException("fatal");
        _ = _innerPublisher.PublishRawAsync(message, metadata, Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.FailFatal(exception)));
            
        var result = await sut.PublishRawAsync(message, metadata, context);
        
        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeFalse();
        result.Error.Should().Be(exception);
    }

    [Fact]
    public async Task PublishRawAsync_RetryExhausted_ReturnsFailFatal()
    {
        var policy = new FixedDelayRetryPolicy(TimeSpan.Zero, 1); // 1 retry
        var sut = new RetryDispatcherInterceptor(_innerPublisher, policy, _circuitBreaker, _enabledLogger);
        
        var message = new OutboxMessage(Guid.NewGuid(), "test", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        var metadata = new MessageMetadata();
        var context = new DispatchContext(CancellationToken.None, 1);
        
        _ = _innerPublisher.PublishRawAsync(message, metadata, Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.FailAndRetry(new InvalidOperationException("fail"))));
            
        var result = await sut.PublishRawAsync(message, metadata, context);
        
        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeFalse();
        result.Error.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task PublishRawAsync_CircuitBreakerOpensDuringRetry_ReturnsFailAndRetryNoIncrement()
    {
        var policy = new FixedDelayRetryPolicy(TimeSpan.Zero, 3);
        var cb = new CircuitBreakerState(2, TimeSpan.FromMinutes(1)); // Opens after 2 failures
        var sut = new RetryDispatcherInterceptor(_innerPublisher, policy, cb, _enabledLogger);
        
        var message = new OutboxMessage(Guid.NewGuid(), "test", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        var metadata = new MessageMetadata();
        var context = new DispatchContext(CancellationToken.None, 1);
        
        _ = _innerPublisher.PublishRawAsync(message, metadata, Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.FailAndRetry(new InvalidOperationException("fail"))));
            
        var result = await sut.PublishRawAsync(message, metadata, context);
        
        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
        result.IncrementRetryCount.Should().BeFalse();
        result.Error.Should().BeOfType<CircuitBreakerOpenException>();
    }

    [Fact]
    public async Task PublishRawAsync_CancellationRequested_ReturnsFailAndRetry_NotFailFatal()
    {
        // ISSUE-ERR1 FIX VERIFICATION: Cancellation must NOT dead-letter the message.
        // Before fix: returned FailFatal → message went to state=4 (dead-letter) during shutdown.
        // After fix: returns FailAndRetry(incrementRetryCount:false) → message stays in state=3.
        var policy = new FixedDelayRetryPolicy(TimeSpan.FromSeconds(10), 3);
        var sut = new RetryDispatcherInterceptor(_innerPublisher, policy, _circuitBreaker, _enabledLogger);
        
        var message = new OutboxMessage(Guid.NewGuid(), "test", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        var metadata = new MessageMetadata();
        var cts = new CancellationTokenSource();
        var context = new DispatchContext(cts.Token, 1);
        
        _ = _innerPublisher.PublishRawAsync(message, metadata, Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.FailAndRetry(new InvalidOperationException("fail"))))
            .AndDoes(_ => cts.Cancel()); // Cancel during the first attempt
            
        var result = await sut.PublishRawAsync(message, metadata, context);
        
        result.Success.Should().BeFalse();
        // ISSUE-ERR1: ShouldRetry MUST be true. Message will be reclaimed by ReclaimStaleMessagesAsync on next startup.
        result.ShouldRetry.Should().BeTrue();
        result.Error.Should().BeOfType<OperationCanceledException>();
    }
}
