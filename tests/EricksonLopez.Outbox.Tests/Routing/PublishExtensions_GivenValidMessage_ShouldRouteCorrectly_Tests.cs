// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox.Persistence;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Routing;

public class PublishExtensions_GivenValidMessage_ShouldRouteCorrectly_Tests
{
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly IOutboxTransactionContext _transaction = Substitute.For<IOutboxTransactionContext>();

    public record TestMessage(string Content);

    [Fact]
    public async Task EnqueueAsync_SingleMessage_CallsStoreAsync_WithAndWithoutCancellationToken()
    {
        var msg = new TestMessage("hello");
        
        // Without CT
        await _outbox.EnqueueAsync(msg, _transaction);
        await _outbox.Received(1).StoreAsync(msg, _transaction, default);

        // With CT
        using var cts = new CancellationTokenSource();
        await _outbox.EnqueueAsync(msg, _transaction, cts.Token);
        await _outbox.Received(1).StoreAsync(msg, _transaction, cts.Token);
    }

    [Fact]
    public async Task EnqueueAsync_ReadOnlyMemory_CallsStoreAsync_WithAndWithoutCancellationToken()
    {
        var messages = new ReadOnlyMemory<TestMessage>(new[] { new TestMessage("m1"), new TestMessage("m2") });
        
        // Without CT
        await _outbox.EnqueueAsync(messages, _transaction);
        await _outbox.Received(1).StoreAsync(messages, _transaction, default);

        // With CT
        using var cts = new CancellationTokenSource();
        await _outbox.EnqueueAsync(messages, _transaction, cts.Token);
        await _outbox.Received(1).StoreAsync(messages, _transaction, cts.Token);
    }

    [Fact]
    public async Task EnqueueAsync_IEnumerable_CallsStoreAsync_WithAndWithoutCancellationToken()
    {
        IEnumerable<TestMessage> messages = new List<TestMessage> { new("m1"), new("m2") };
        
        // Without CT
        await _outbox.EnqueueAsync(messages, _transaction);
        await _outbox.Received(1).StoreAsync(messages, _transaction, default);

        // With CT
        using var cts = new CancellationTokenSource();
        await _outbox.EnqueueAsync(messages, _transaction, cts.Token);
        await _outbox.Received(1).StoreAsync(messages, _transaction, cts.Token);
    }

    [Fact]
    public async Task EnqueueAsync_WithMetadata_CallsStoreAsync_WithDefaultAndExplicitDeliverAt()
    {
        var msg = new TestMessage("hello");
        var metadata = new OutboxMessageMetadata("corr-1", "caus-1", "TestMessageType");
        
        // With default deliverAt = null and default CT
        await _outbox.EnqueueAsync(msg, _transaction, metadata);
        await _outbox.Received(1).StoreAsync(msg, _transaction, metadata, null, default);

        // With explicit deliverAt and CT
        var deliverAt = DateTimeOffset.UtcNow.AddMinutes(5);
        using var cts = new CancellationTokenSource();
        await _outbox.EnqueueAsync(msg, _transaction, metadata, deliverAt, cts.Token);
        await _outbox.Received(1).StoreAsync(msg, _transaction, metadata, deliverAt, cts.Token);
    }

    [Fact]
    public async Task EnqueueAsync_ThrowsOnNullArguments()
    {
        var msg = new TestMessage("hello");

        Func<Task> act1 = async () => await OutboxPublishExtensions.EnqueueAsync<TestMessage>(null!, msg, _transaction);
        await act1.Should().ThrowAsync<ArgumentNullException>().WithParameterName("outbox");

        Func<Task> act2 = async () => await _outbox.EnqueueAsync<TestMessage>((TestMessage)null!, _transaction);
        await act2.Should().ThrowAsync<ArgumentNullException>().WithParameterName("message");

        Func<Task> act3 = async () => await _outbox.EnqueueAsync(msg, null!);
        await act3.Should().ThrowAsync<ArgumentNullException>().WithParameterName("transaction");

        Func<Task> act4 = async () => await OutboxPublishExtensions.EnqueueAsync<TestMessage>(null!, new ReadOnlyMemory<TestMessage>(new[] { msg }), _transaction);
        await act4.Should().ThrowAsync<ArgumentNullException>().WithParameterName("outbox");

        Func<Task> act5 = async () => await _outbox.EnqueueAsync(new ReadOnlyMemory<TestMessage>(new[] { msg }), null!);
        await act5.Should().ThrowAsync<ArgumentNullException>().WithParameterName("transaction");

        Func<Task> act6 = async () => await OutboxPublishExtensions.EnqueueAsync<TestMessage>(null!, (IEnumerable<TestMessage>)new List<TestMessage> { msg }, _transaction);
        await act6.Should().ThrowAsync<ArgumentNullException>().WithParameterName("outbox");

        Func<Task> act7 = async () => await _outbox.EnqueueAsync<TestMessage>((IEnumerable<TestMessage>)null!, _transaction);
        await act7.Should().ThrowAsync<ArgumentNullException>().WithParameterName("messages");

        Func<Task> act8 = async () => await _outbox.EnqueueAsync<TestMessage>((IEnumerable<TestMessage>)new List<TestMessage> { msg }, null!);
        await act8.Should().ThrowAsync<ArgumentNullException>().WithParameterName("transaction");

        Func<Task> act9 = async () => await OutboxPublishExtensions.EnqueueAsync<TestMessage>(null!, msg, _transaction, default(OutboxMessageMetadata));
        await act9.Should().ThrowAsync<ArgumentNullException>().WithParameterName("outbox");

        Func<Task> act10 = async () => await _outbox.EnqueueAsync<TestMessage>(null!, _transaction, default(OutboxMessageMetadata));
        await act10.Should().ThrowAsync<ArgumentNullException>().WithParameterName("message");

        Func<Task> act11 = async () => await _outbox.EnqueueAsync(msg, null!, default(OutboxMessageMetadata));
        await act11.Should().ThrowAsync<ArgumentNullException>().WithParameterName("transaction");
    }

    [Fact]
    public void Publish_Should_Return_OutboxMessageBuilder_And_Validate_Null_Outbox()
    {
        var msg = new TestMessage("hello");

        var act = () => OutboxPublishExtensions.Publish<TestMessage>(null!, msg);
        act.Should().Throw<ArgumentNullException>().WithParameterName("outbox");

        var builder = _outbox.Publish(msg);
        builder.Should().NotBeNull();
    }
}

