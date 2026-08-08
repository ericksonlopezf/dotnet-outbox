#pragma warning disable CS8600, CS8602, CA2012
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Collections.Generic;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Pipeline;
using EricksonLopez.Outbox.Dispatcher;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using EricksonLopez.Outbox.Diagnostics;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

public class OutboxChannelStrykerTests
{
    private readonly OutboxChannel _channel;
    private readonly IBrokerPublisher _publisher = Substitute.For<IBrokerPublisher>();
    private readonly IOutboxRepository _repository = Substitute.For<IOutboxRepository>();
    private readonly IDeadLetterRepository _dlqRepository = Substitute.For<IDeadLetterRepository>();
    private readonly IServiceScopeFactory _scopeFactory = Substitute.For<IServiceScopeFactory>();
    private readonly IServiceScope _scope = Substitute.For<IServiceScope>();
    private readonly IServiceProvider _serviceProvider = Substitute.For<IServiceProvider>();

    public OutboxChannelStrykerTests()
    {
        _scopeFactory.CreateScope().Returns(_scope);
        _scope.ServiceProvider.Returns(_serviceProvider);
        _serviceProvider.GetService(typeof(IOutboxRepository)).Returns(_repository);
        _serviceProvider.GetService(typeof(IDeadLetterRepository)).Returns(_dlqRepository);
        _serviceProvider.GetService(typeof(IEnumerable<IOutboxMiddleware>)).Returns(Array.Empty<IOutboxMiddleware>());

        var options = Options.Create(new OutboxDispatcherOptions());
        var runtimeOptions = Options.Create(new OutboxRuntimeOptions());

        _channel = new OutboxChannel(
            NullLogger<OutboxChannel>.Instance,
            _publisher,
            options,
            runtimeOptions,
            new OutboxMetrics(Substitute.For<System.Diagnostics.Metrics.IMeterFactory>()),
            _scopeFactory,
            new DefaultErrorSanitizer()
        );
    }

    [Fact]
    public async Task ProcessMessagesAsync_WithNullHeaderValue_IgnoresHeader()
    {
        // Arrange
        var jsonHeaders = "{\"key1\":null, \"key2\":\"value2\"}";
        var msg = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, JsonSerializer.SerializeToUtf8Bytes(JsonDocument.Parse(jsonHeaders).RootElement), DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        
        _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<MessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(DispatchResult.Ok());

        await _channel.WriteAsync(msg, CancellationToken.None);
        // Complete the channel so processing stops
        var channelField = typeof(OutboxChannel).GetField("_channel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var innerChannel = (System.Threading.Channels.Channel<OutboxMessage>)channelField.GetValue(_channel);
        innerChannel.Writer.Complete();

        // Act
        await _channel.ProcessMessagesAsync(CancellationToken.None);

        // Assert
        // We ensure that the key2 is parsed, and key1 is ignored without throwing an exception.
        // It's tested indirectly by the fact that ProcessMessagesAsync completes successfully 
        // without logging a header deserialization error.
        await _publisher.Received(1).PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<MessageMetadata>(), Arg.Any<DispatchContext>());
    }

    [Fact]
    public async Task ProcessMessagesAsync_Failed_NoRetry_MarksAsFailed()
    {
        // Arrange
        var msg = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        
        _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<MessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(DispatchResult.FailFatal(new InvalidOperationException("Fatal error")));

        await _channel.WriteAsync(msg, CancellationToken.None);
        var channelField = typeof(OutboxChannel).GetField("_channel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var innerChannel = (System.Threading.Channels.Channel<OutboxMessage>)channelField.GetValue(_channel);
        innerChannel.Writer.Complete();

        // Act
        await _channel.ProcessMessagesAsync(CancellationToken.None);

        // Assert
        await _repository.Received(1).MarkAsFailedAsync(msg, "Fatal error", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessagesAsync_Failed_MaxRetriesReached_MarksAsDeadLetter()
    {
        // Arrange
        var msg = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 9, null);
        
        _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<MessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(DispatchResult.FailAndRetry(new InvalidOperationException("Transient error")));

        await _channel.WriteAsync(msg, CancellationToken.None);
        var channelField = typeof(OutboxChannel).GetField("_channel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var innerChannel = (System.Threading.Channels.Channel<OutboxMessage>)channelField.GetValue(_channel);
        innerChannel.Writer.Complete();

        // Act
        await _channel.ProcessMessagesAsync(CancellationToken.None);

        // Assert
        await _repository.Received(1).MarkAsFailedAsync(msg, "Transient error", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessagesAsync_Failed_NotMaxRetries_ShouldRetry_MarksAsFailedNotDeadLetter()
    {
        // Arrange
        var msg = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 1, null);
        
        _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<MessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(DispatchResult.FailAndRetry(new InvalidOperationException("Transient error")));

        await _channel.WriteAsync(msg, CancellationToken.None);
        var channelField = typeof(OutboxChannel).GetField("_channel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var innerChannel = (System.Threading.Channels.Channel<OutboxMessage>)channelField.GetValue(_channel);
        innerChannel.Writer.Complete();

        // Act
        await _channel.ProcessMessagesAsync(CancellationToken.None);

        // Assert
        await _repository.Received(1).MarkAsFailedAsync(msg, "Transient error", false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessagesAsync_WithInvalidJsonHeaders_FailsFatal()
    {
        // Arrange
        var jsonHeaders = "invalid json";
        var msg = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, System.Text.Encoding.UTF8.GetBytes(jsonHeaders), DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        
        await _channel.WriteAsync(msg, CancellationToken.None);
        var channelField = typeof(OutboxChannel).GetField("_channel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var innerChannel = (System.Threading.Channels.Channel<OutboxMessage>)channelField.GetValue(_channel);
        innerChannel.Writer.Complete();

        // Act
        await _channel.ProcessMessagesAsync(CancellationToken.None);

        // Assert
        await _repository.Received(1).MarkAsFailedAsync(msg, Arg.Is<string>(s => s.Contains("deserialize headers")), true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessagesAsync_WithJsonArrayHeaders_IgnoresHeaders()
    {
        // Arrange
        var jsonHeaders = "[1, 2, 3]";
        var msg = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, System.Text.Encoding.UTF8.GetBytes(jsonHeaders), DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        
        _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<MessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(DispatchResult.Ok());

        await _channel.WriteAsync(msg, CancellationToken.None);
        var channelField = typeof(OutboxChannel).GetField("_channel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var innerChannel = (System.Threading.Channels.Channel<OutboxMessage>)channelField.GetValue(_channel);
        innerChannel.Writer.Complete();

        // Act
        await _channel.ProcessMessagesAsync(CancellationToken.None);

        // Assert
        // Should not fail, just ignores the headers
        await _publisher.Received(1).PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<MessageMetadata>(), Arg.Any<DispatchContext>());
    }

    [Fact]
    public async Task ProcessMessagesAsync_WithEmptyJsonHeaders_Works()
    {
        // Arrange
        var jsonHeaders = "{}";
        var msg = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, System.Text.Encoding.UTF8.GetBytes(jsonHeaders), DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        
        _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<MessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(DispatchResult.Ok());

        await _channel.WriteAsync(msg, CancellationToken.None);
        var channelField = typeof(OutboxChannel).GetField("_channel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var innerChannel = (System.Threading.Channels.Channel<OutboxMessage>)channelField.GetValue(_channel);
        innerChannel.Writer.Complete();

        // Act
        await _channel.ProcessMessagesAsync(CancellationToken.None);

        // Assert
        await _publisher.Received(1).PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<MessageMetadata>(), Arg.Any<DispatchContext>());
    }
}
