// Copyright © Erickson Lopez. MIT License.
#pragma warning disable CA2012
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox.Retry;
using EricksonLopez.Result;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Resilience;

public sealed class RetryDispatcherInterceptorTests
{
    private readonly IBrokerPublisher _innerPublisher = Substitute.For<IBrokerPublisher>();

    private sealed class EnabledLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    private readonly ILogger<RetryDispatcherInterceptor> _enabledLogger = new EnabledLogger<RetryDispatcherInterceptor>();

    private static OutboxMessage CreateTestMessage() =>
        new(Guid.NewGuid(), "test-alias", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);

    private static OutboxMessageMetadata CreateTestMetadata() => new();

    [Fact]
    public void Constructor_NullDelayFunc_FallsBackToTaskDelay()
    {
        var policy = new FixedDelayRetryPolicy(TimeSpan.Zero, 1);
        var cb = new CircuitBreakerState();
        var interceptor = new RetryDispatcherInterceptor(
            _innerPublisher,
            policy,
            cb,
            _enabledLogger,
            delayFunc: null);

        interceptor.Should().NotBeNull();
        interceptor.Policy.Should().BeSameAs(policy);
        interceptor.CircuitBreaker.Should().BeSameAs(cb);
    }

    [Fact]
    public void Constructor_Public_InstantiatesSuccessfully()
    {
        var policy = new FixedDelayRetryPolicy(TimeSpan.Zero, 1);
        var cb = new CircuitBreakerState();
        var interceptor = new RetryDispatcherInterceptor(
            _innerPublisher,
            policy,
            cb,
            _enabledLogger);

        interceptor.Should().NotBeNull();
        interceptor.Policy.Should().BeSameAs(policy);
        interceptor.CircuitBreaker.Should().BeSameAs(cb);
    }

    [Fact]
    public async Task PublishRawAsync_WhenCustomDelayFuncInjected_InvokesCustomDelayFunc()
    {
        TimeSpan? recordedDelay = null;
        Func<TimeSpan, CancellationToken, Task> customDelay = (delay, ct) =>
        {
            recordedDelay = delay;
            return Task.CompletedTask;
        };

        var interceptor = new RetryDispatcherInterceptor(
            _innerPublisher,
            new FixedDelayRetryPolicy(TimeSpan.FromMilliseconds(42), 2),
            new CircuitBreakerState(),
            _enabledLogger,
            delayFunc: customDelay);

        _innerPublisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(
                new ValueTask<DispatchResult>(DispatchResult.FailAndRetry(new InvalidOperationException("transient"))),
                new ValueTask<DispatchResult>(DispatchResult.Ok()));

        var message = CreateTestMessage();
        var metadata = CreateTestMetadata();
        var context = new DispatchContext(CancellationToken.None, 1);

        var result = await interceptor.PublishRawAsync(message, metadata, context);

        result.Success.Should().BeTrue();
        recordedDelay.Should().Be(TimeSpan.FromMilliseconds(42));
    }

    [Fact]
    public async Task PublishRawAsync_CircuitBreakerInitiallyOpen_ReturnsFailAndRetryWithoutCallingInner()
    {
        var cb = new CircuitBreakerState(1, TimeSpan.FromMinutes(1));
        cb.RecordFailure(); // Now Open

        var sut = new RetryDispatcherInterceptor(_innerPublisher, new FixedDelayRetryPolicy(TimeSpan.Zero, 3), cb, _enabledLogger);

        var message = CreateTestMessage();
        var metadata = CreateTestMetadata();
        var context = new DispatchContext(CancellationToken.None, 1);

        var result = await sut.PublishRawAsync(message, metadata, context);

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
        result.IncrementRetryCount.Should().BeFalse();
        result.Error.Should().BeOfType<CircuitBreakerOpenException>()
            .Which.Message.Should().Be("Circuit breaker is open.");

        await _innerPublisher.DidNotReceiveWithAnyArgs().PublishRawAsync(default!, default!, default);
    }

    [Fact]
    public async Task PublishRawAsync_CancellationTokenAlreadyCancelled_ReturnsFailAndRetry()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sut = new RetryDispatcherInterceptor(_innerPublisher, new FixedDelayRetryPolicy(TimeSpan.Zero, 3), new CircuitBreakerState(), _enabledLogger);

        var message = CreateTestMessage();
        var metadata = CreateTestMetadata();
        var context = new DispatchContext(cts.Token, 1);

        var result = await sut.PublishRawAsync(message, metadata, context);

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
        result.IncrementRetryCount.Should().BeFalse();
        result.Error.Should().BeOfType<OperationCanceledException>();

        await _innerPublisher.DidNotReceiveWithAnyArgs().PublishRawAsync(default!, default!, default);
    }

    [Fact]
    public async Task PublishRawAsync_SuccessOnFirstTry_RecordsSuccessAndReturns()
    {
        var cb = new CircuitBreakerState(3);
        cb.RecordFailure(); // 1 failure recorded

        var sut = new RetryDispatcherInterceptor(_innerPublisher, new FixedDelayRetryPolicy(TimeSpan.Zero, 3), cb, _enabledLogger);

        var message = CreateTestMessage();
        var metadata = CreateTestMetadata();
        var context = new DispatchContext(CancellationToken.None, 1);

        int recordedAttempt = 0;
        _innerPublisher.PublishRawAsync(message, metadata, Arg.Any<DispatchContext>())
            .Returns(callInfo =>
            {
                var ctx = callInfo.Arg<DispatchContext>();
                recordedAttempt = ctx.Attempt;
                return new ValueTask<DispatchResult>(DispatchResult.Ok());
            });

        var result = await sut.PublishRawAsync(message, metadata, context);

        result.Success.Should().BeTrue();
        recordedAttempt.Should().Be(1);

        // Circuit breaker success resets failure count
        cb.RecordFailure();
        cb.RecordFailure();
        cb.State.Should().Be(CircuitState.Closed); // only 2 failures after reset, not 3
    }

    [Fact]
    public async Task PublishRawAsync_TransientFailureThenSuccess_ExecutesDelayAndIncrementsAttempt()
    {
        var delays = new List<TimeSpan>();
        Task CustomDelay(TimeSpan d, CancellationToken ct)
        {
            delays.Add(d);
            return Task.CompletedTask;
        }

        var policy = new ExponentialBackoffRetryPolicy(TimeSpan.FromMilliseconds(100), 5, Factor: 2.0);
        var cb = new CircuitBreakerState(5);
        var sut = new RetryDispatcherInterceptor(_innerPublisher, policy, cb, _enabledLogger, CustomDelay);

        var message = CreateTestMessage();
        var metadata = CreateTestMetadata();
        var context = new DispatchContext(CancellationToken.None, 1);

        var attempts = new List<int>();
        _innerPublisher.PublishRawAsync(message, metadata, Arg.Any<DispatchContext>())
            .Returns(callInfo =>
            {
                var ctx = callInfo.Arg<DispatchContext>();
                attempts.Add(ctx.Attempt);

                if (ctx.Attempt < 3)
                {
                    return new ValueTask<DispatchResult>(DispatchResult.FailAndRetry(new InvalidOperationException($"Attempt {ctx.Attempt} failed")));
                }

                return new ValueTask<DispatchResult>(DispatchResult.Ok());
            });

        var result = await sut.PublishRawAsync(message, metadata, context);

        result.Success.Should().BeTrue();
        attempts.Should().Equal(1, 2, 3);
        delays.Should().Equal(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public async Task PublishRawAsync_FatalError_DoesNotRetryAndReturnsImmediately()
    {
        var sut = new RetryDispatcherInterceptor(_innerPublisher, new FixedDelayRetryPolicy(TimeSpan.Zero, 5), new CircuitBreakerState(), _enabledLogger);

        var message = CreateTestMessage();
        var metadata = CreateTestMetadata();
        var context = new DispatchContext(CancellationToken.None, 1);

        var fatalException = new InvalidOperationException("Fatal broker config error");
        _innerPublisher.PublishRawAsync(message, metadata, Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.FailFatal(fatalException)));

        var result = await sut.PublishRawAsync(message, metadata, context);

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeFalse();
        result.Error.Should().BeSameAs(fatalException);

        await _innerPublisher.Received(1).PublishRawAsync(message, metadata, Arg.Any<DispatchContext>());
    }

    [Fact]
    public async Task PublishRawAsync_CircuitBreakerOpensDuringRetry_AbortsRetryLoop()
    {
        var cb = new CircuitBreakerState(2, TimeSpan.FromMinutes(1)); // Opens on 2nd failure
        var policy = new FixedDelayRetryPolicy(TimeSpan.Zero, 5);
        var sut = new RetryDispatcherInterceptor(_innerPublisher, policy, cb, _enabledLogger);

        var message = CreateTestMessage();
        var metadata = CreateTestMetadata();
        var context = new DispatchContext(CancellationToken.None, 1);

        _innerPublisher.PublishRawAsync(message, metadata, Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.FailAndRetry(new InvalidOperationException("Fail"))));

        var result = await sut.PublishRawAsync(message, metadata, context);

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
        result.IncrementRetryCount.Should().BeFalse();
        result.Error.Should().BeOfType<CircuitBreakerOpenException>()
            .Which.Message.Should().Be("Circuit breaker opened.");

        await _innerPublisher.Received(2).PublishRawAsync(message, metadata, Arg.Any<DispatchContext>());
    }

    [Fact]
    public async Task PublishRawAsync_RetryPolicyExhausted_ReturnsFailFatal()
    {
        var policy = new FixedDelayRetryPolicy(TimeSpan.Zero, 2);
        var sut = new RetryDispatcherInterceptor(_innerPublisher, policy, new CircuitBreakerState(10), _enabledLogger);

        var message = CreateTestMessage();
        var metadata = CreateTestMetadata();
        var context = new DispatchContext(CancellationToken.None, 1);

        var innerEx = new InvalidOperationException("Transient network outage");
        _innerPublisher.PublishRawAsync(message, metadata, Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.FailAndRetry(innerEx)));

        var result = await sut.PublishRawAsync(message, metadata, context);

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeFalse();
        result.Error.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be("Retry policy exhausted after 2 attempts.");
        result.Error!.InnerException.Should().BeSameAs(innerEx);

        await _innerPublisher.Received(2).PublishRawAsync(message, metadata, Arg.Any<DispatchContext>());
    }

    [Fact]
    public async Task PublishRawAsync_DelayCancelled_CatchesAndReturnsFailAndRetry()
    {
        Task CancelDuringDelay(TimeSpan d, CancellationToken ct)
        {
            throw new OperationCanceledException();
        }

        var policy = new FixedDelayRetryPolicy(TimeSpan.FromSeconds(1), 3);
        var sut = new RetryDispatcherInterceptor(_innerPublisher, policy, new CircuitBreakerState(10), _enabledLogger, CancelDuringDelay);

        var message = CreateTestMessage();
        var metadata = CreateTestMetadata();
        var context = new DispatchContext(CancellationToken.None, 1);

        _innerPublisher.PublishRawAsync(message, metadata, Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.FailAndRetry(new InvalidOperationException("Fail 1"))));

        var result = await sut.PublishRawAsync(message, metadata, context);

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
        result.IncrementRetryCount.Should().BeFalse();
        result.Error.Should().BeOfType<OperationCanceledException>();

        await _innerPublisher.Received(1).PublishRawAsync(message, metadata, Arg.Any<DispatchContext>());
    }

    [Fact]
    public async Task PublishRawAsync_DisabledLogger_ExercisesAllDisabledLogPaths()
    {
        var nullLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<RetryDispatcherInterceptor>.Instance;

        // 1. Initial CB open with null logger
        var cbOpen = new CircuitBreakerState(1, TimeSpan.FromMinutes(1));
        cbOpen.RecordFailure();
        var sut1 = new RetryDispatcherInterceptor(_innerPublisher, new FixedDelayRetryPolicy(TimeSpan.Zero, 1), cbOpen, nullLogger);
        await sut1.PublishRawAsync(CreateTestMessage(), CreateTestMetadata(), new DispatchContext(CancellationToken.None, 1));

        // 2. Fatal error with null logger
        var sut2 = new RetryDispatcherInterceptor(_innerPublisher, new FixedDelayRetryPolicy(TimeSpan.Zero, 1), new CircuitBreakerState(), nullLogger);
        _innerPublisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.FailFatal(new InvalidOperationException("fatal"))));
        await sut2.PublishRawAsync(CreateTestMessage(), CreateTestMetadata(), new DispatchContext(CancellationToken.None, 1));

        // 3. CB opened during retry with null logger
        var cbOpening = new CircuitBreakerState(1, TimeSpan.FromMinutes(1));
        var sut3 = new RetryDispatcherInterceptor(_innerPublisher, new FixedDelayRetryPolicy(TimeSpan.Zero, 2), cbOpening, nullLogger);
        _innerPublisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.FailAndRetry(new InvalidOperationException("transient"))));
        await sut3.PublishRawAsync(CreateTestMessage(), CreateTestMetadata(), new DispatchContext(CancellationToken.None, 1));

        // 4. Retry exhausted with null logger
        var sut4 = new RetryDispatcherInterceptor(_innerPublisher, new FixedDelayRetryPolicy(TimeSpan.Zero, 1), new CircuitBreakerState(10), nullLogger);
        _innerPublisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.FailAndRetry(new InvalidOperationException("exhausted"))));
        await sut4.PublishRawAsync(CreateTestMessage(), CreateTestMetadata(), new DispatchContext(CancellationToken.None, 1));

        // 5. Transient failure + retry delay + success with null logger (covers LogPublishFailed disabled)
        var sut5 = new RetryDispatcherInterceptor(_innerPublisher, new FixedDelayRetryPolicy(TimeSpan.Zero, 3), new CircuitBreakerState(10), nullLogger,
            (d, ct) => Task.CompletedTask);
        int callCount = 0;
        _innerPublisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(callInfo =>
            {
                callCount++;
                return callCount == 1
                    ? new ValueTask<DispatchResult>(DispatchResult.FailAndRetry(new InvalidOperationException("fail 1")))
                    : new ValueTask<DispatchResult>(DispatchResult.Ok());
            });
        await sut5.PublishRawAsync(CreateTestMessage(), CreateTestMetadata(), new DispatchContext(CancellationToken.None, 1));

        // 6. Transient failure + delay + cancel with null logger
        using var cts = new CancellationTokenSource();
        var sut6 = new RetryDispatcherInterceptor(_innerPublisher, new FixedDelayRetryPolicy(TimeSpan.FromMilliseconds(10), 3), new CircuitBreakerState(10), nullLogger,
            (d, ct) => { cts.Cancel(); throw new OperationCanceledException(ct); });
        _innerPublisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.FailAndRetry(new InvalidOperationException("fail"))));
        await sut6.PublishRawAsync(CreateTestMessage(), CreateTestMetadata(), new DispatchContext(cts.Token, 1));
    }
}





