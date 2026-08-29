// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using AwesomeAssertions;
using System.Threading.Channels;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Diagnostics;
using EricksonLopez.Outbox.Dispatcher;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Pipeline;
using EricksonLopez.Outbox.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Threading.Tasks;
using EricksonLopez.Outbox.Tests.Infrastructure;
using Xunit;

#pragma warning disable CA2012, CS8600, CS8602
namespace EricksonLopez.Outbox.Tests.Delivery;

[Collection("ActivitySource")]
public partial class OutboxChannelTests
{

/// <summary>
/// Completes the channel writer via reflection so ProcessMessagesAsync exits naturally
/// instead of waiting for a CancellationToken timeout. This makes tests deterministic
/// and avoids timing flakiness under parallel test execution.
/// </summary>
private static Microsoft.Extensions.DependencyInjection.IServiceScopeFactory FakeScopeFactory(IServiceProvider provider)
{
    var scope = Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScope>();
    scope.ServiceProvider.Returns(provider);
    var factory = Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();
    factory.CreateScope().Returns(scope);
    return factory;
}

private static EricksonLopez.Outbox.Diagnostics.IErrorSanitizer FakeErrorSanitizer()
{
    var s = NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>();
    s.Sanitize(Arg.Any<Exception>()).Returns(x => x.Arg<Exception>().Message);
    return s;
}

private static void CompleteWriter(OutboxChannel channel)
{
    channel.Complete();
}

private static OutboxChannel CreateTestChannel(
    IBrokerPublisher publisher,
    IOutboxRepository repo,
    IDeadLetterRepository? dlqRepo = null,
    ILogger<OutboxChannel>? logger = null,
    OutboxDispatcherOptions? dispatcherOptions = null,
    OutboxRuntimeOptions? runtimeOptions = null)
{
    var sc = new ServiceCollection();
    sc.AddScoped(_ => repo);
    if (dlqRepo != null)
    {
        sc.AddScoped<IDeadLetterRepository>(_ => dlqRepo);
    }
    var services = sc.BuildServiceProvider();

    return new OutboxChannel(
        logger ?? NullLogger<OutboxChannel>.Instance,
        publisher,
        Options.Create(dispatcherOptions ?? new OutboxDispatcherOptions { ChannelCapacity = 10 }),
        Options.Create(runtimeOptions ?? new OutboxRuntimeOptions()),
        new OutboxMetrics(),
        FakeScopeFactory(services),
        FakeErrorSanitizer(),
        TimeProvider.System);
}

private sealed class FakeChannelLogger : Microsoft.Extensions.Logging.ILogger<OutboxChannel>
{

    public readonly List<Microsoft.Extensions.Logging.EventId> LoggedEvents = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var msg = formatter(state, exception);
        if (exception != null) msg += " | Exception: " + exception.Message;

        LoggedEvents.Add(eventId);
    }
}

    [Theory]
    [InlineData(1, 10, true)]
    [InlineData(4, 20, false)]
    public void CreateChannelOptions_WhenConfigured_ReflectsDispatcherOptions(int dop, int capacity, bool expectedSingleReader)
    {
        var options = new OutboxDispatcherOptions { ChannelCapacity = capacity, MaxDegreeOfParallelism = dop };
        var channelOptions = OutboxChannel.CreateChannelOptions(options);

        channelOptions.Capacity.Should().Be(capacity);
        channelOptions.FullMode.Should().Be(BoundedChannelFullMode.Wait);
        channelOptions.SingleWriter.Should().BeTrue();
        channelOptions.SingleReader.Should().Be(expectedSingleReader);
    }

    [Fact]
    public void HeadersDeserializationCache_Reset_ClearsCachedMemoryAndDict()
    {
        var cache = new HeadersDeserializationCache();
        var bytes = System.Text.Encoding.UTF8.GetBytes("{\"k\":\"v\"}");
        var dict = new Dictionary<string, string> { { "k", "v" } };

        cache.Swap(bytes, dict);
        cache.LastHeadersMemory.Should().NotBeNull();
        cache.LastHeadersDict.Should().NotBeNull();

        cache.Reset();
        cache.LastHeadersMemory.Should().BeNull();
        cache.LastHeadersDict.Should().BeNull();
    }

    [Theory]
    [InlineData("[1, 2, 3]")]
    [InlineData("[{\"k\":\"v\"}]")]
    [InlineData("\"a string\"")]
    [InlineData("123")]
    [InlineData("")]
    public void ParseHeadersFast_WhenNotJsonObject_LeavesHeadersEmpty(string json)
    {
        var span = System.Text.Encoding.UTF8.GetBytes(json);
        var dict = new Dictionary<string, string>();
        OutboxChannel.ParseHeadersFast(span, dict);
        dict.Should().BeEmpty();
    }

    [Fact]
    public void ParseHeadersFast_WhenWhitespaceOnly_ThrowsJsonException()
    {
        var span = System.Text.Encoding.UTF8.GetBytes("   ");
        var dict = new Dictionary<string, string>();
        Action act = () => OutboxChannel.ParseHeadersFast(span, dict);
        act.Should().Throw<System.Text.Json.JsonException>();
    }

    [Fact]
    public void ParseHeadersFast_WhenEmptyObject_LeavesHeadersEmpty()
    {
        var span = System.Text.Encoding.UTF8.GetBytes("{}");
        var dict = new Dictionary<string, string>();
        OutboxChannel.ParseHeadersFast(span, dict);
        dict.Should().BeEmpty();
    }

    [Fact]
    public void ParseHeadersFast_WhenValidObject_PopulatesHeaders()
    {
        var span = System.Text.Encoding.UTF8.GetBytes("{\"k1\":\"v1\",\"k2\":\"v2\"}");
        var dict = new Dictionary<string, string>();
        OutboxChannel.ParseHeadersFast(span, dict);
        dict.Should().HaveCount(2);
        dict["k1"].Should().Be("v1");
        dict["k2"].Should().Be("v2");
    }

    [Fact]
    public void ParseHeadersFast_WhenMultipleObjects_StopsAtFirstEndObject()
    {
        var span = System.Text.Encoding.UTF8.GetBytes("{\"k1\":\"v1\"}{\"k2\":\"v2\"}");
        var dict = new Dictionary<string, string>();
        OutboxChannel.ParseHeadersFast(span, dict);
        dict.Should().HaveCount(1);
        dict["k1"].Should().Be("v1");
        dict.ContainsKey("k2").Should().BeFalse();
    }

    [Fact]
    public void ParseHeadersFast_WhenValueIsNull_DoesNotAddToHeaders()
    {
        var span = System.Text.Encoding.UTF8.GetBytes("{\"k1\":null,\"k2\":\"v2\"}");
        var dict = new Dictionary<string, string>();
        OutboxChannel.ParseHeadersFast(span, dict);
        dict.Should().HaveCount(1);
        dict["k2"].Should().Be("v2");
        dict.ContainsKey("k1").Should().BeFalse();
    }

    [Fact]
    public void ParseHeadersFast_WhenNestedObjectPresent_HandlesTokensCorrectly()
    {
        var span = System.Text.Encoding.UTF8.GetBytes("{\"k1\":\"v1\",\"nested\":{\"inner\":\"val\"},\"k2\":\"v2\"}");
        var dict = new Dictionary<string, string>();
        OutboxChannel.ParseHeadersFast(span, dict);
        dict.Should().ContainKey("k1");
        dict["k1"].Should().Be("v1");
        dict.Should().ContainKey("k2");
        dict["k2"].Should().Be("v2");
    }

    [Fact]
    public void ParseHeadersFast_WhenArrayPresent_HandlesTokensCorrectly()
    {
        var span = System.Text.Encoding.UTF8.GetBytes("{\"k1\":\"v1\",\"arr\":[1,2,3],\"k2\":\"v2\"}");
        var dict = new Dictionary<string, string>();
        OutboxChannel.ParseHeadersFast(span, dict);
        dict.Should().ContainKey("k1");
        dict["k1"].Should().Be("v1");
        dict.Should().ContainKey("k2");
        dict["k2"].Should().Be("v2");
    }

    [Fact]
    public void ParseHeadersFast_WhenIncompleteJsonObject_ThrowsJsonException()
    {
        var span = System.Text.Encoding.UTF8.GetBytes("{\"k1\":");
        var dict = new Dictionary<string, string>();
        Action act = () => OutboxChannel.ParseHeadersFast(span, dict);
        act.Should().Throw<System.Text.Json.JsonException>();
    }

    [Fact]
    public async Task FillBatchFast_WhenTicksElapsedIs50ms_BreaksLoop()
    {
        var channel = CreateTestChannel(Substitute.For<IBrokerPublisher>(), Substitute.For<IOutboxRepository>());
        var msg1 = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        var msg2 = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);

        await channel.WriteAsync(msg1, CancellationToken.None);
        await channel.WriteAsync(msg2, CancellationToken.None);

        var batch = new List<OutboxMessage>();
        // startTicks simulates exactly 50ms elapsed
        channel.FillBatchFast(batch, Environment.TickCount64 - 50);

        batch.Should().HaveCount(1);
    }

    [Fact]
    public async Task ProcessMessagesAsync_ConcurrentConsumersContention_HandlesEmptyBatchesGracefully()
    {
        var channel = CreateTestChannel(
            Substitute.For<IBrokerPublisher>(),
            Substitute.For<IOutboxRepository>(),
            dispatcherOptions: new OutboxDispatcherOptions { ChannelCapacity = 1000, MaxDegreeOfParallelism = 10 });

        using var cts = new CancellationTokenSource();

        // Spawn 10 concurrent consumers running ProcessMessagesAsync
        var consumerTasks = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(async () =>
            {
                try
                {
                    await channel.ProcessMessagesAsync(cts.Token);
                }
                catch (OperationCanceledException) { }
            }))
            .ToArray();

        // Quickly write and drain messages
        for (int i = 0; i < 200; i++)
        {
            var msg = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
            await channel.WriteAsync(msg, CancellationToken.None);
        }

        channel.Complete();
        await Task.WhenAll(consumerTasks);
    }

    [Fact]
    public async Task ProcessMessagesAsync_WhenBatchCountIsZero_SkipsScopeAndContinues()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var channel = new OutboxChannel(
            NullLogger<OutboxChannel>.Instance,
            Substitute.For<IBrokerPublisher>(),
            Options.Create(new OutboxDispatcherOptions()),
            Options.Create(new OutboxRuntimeOptions()),
            new OutboxMetrics(),
            scopeFactory,
            FakeErrorSanitizer(),
            TimeProvider.System);

        var customChannel = new EmptyBatchChannel();
        ReflectionTestHelper.SetFieldValue(channel, "_channel", customChannel);

        await channel.ProcessMessagesAsync(CancellationToken.None);

        scopeFactory.DidNotReceive().CreateScope();
    }

    private sealed class EmptyBatchChannel : Channel<OutboxMessage>
    {
        public EmptyBatchChannel()
        {
            Reader = new EmptyBatchReader();
            Writer = Channel.CreateUnbounded<OutboxMessage>().Writer;
        }

        private sealed class EmptyBatchReader : ChannelReader<OutboxMessage>
        {
            private int _callCount;

            public override int Count => 0;
            public override bool CanCount => true;

            public override bool TryRead(out OutboxMessage item)
            {
                item = default!;
                return false;
            }

            public override ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
            {
                return ValueTask.FromResult(Interlocked.Increment(ref _callCount) == 1);
            }
        }
    }
}



