// Copyright © Erickson Lopez. MIT License.
using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Brighter;
using EricksonLopez.Outbox.Persistence;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Paramore.Brighter;
using Xunit;

namespace EricksonLopez.Outbox.Brighter.Tests;

public class OutboxMessageProducerTests
{
    [Fact]
    public void Constructor_NullOutbox_ThrowsArgumentNullException()
    {
        var act = () => new OutboxMessageProducer(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("outbox");
    }

    [Fact]
    public void Properties_SetAndGet_Correctly()
    {
        var outbox = Substitute.For<IOutbox>();
        var producer = new OutboxMessageProducer(outbox);

        var requestContext = Substitute.For<IRequestContext>();
        producer.RequestContext = requestContext;
        producer.RequestContext.Should().BeSameAs(requestContext);

        var publication = new Publication { Topic = new RoutingKey("custom-topic") };
        producer.Publication = publication;
        producer.Publication.Should().BeSameAs(publication);

        var actNullPub = () => producer.Publication = null!;
        actNullPub.Should().Throw<ArgumentNullException>().WithParameterName("value");

        using var activity = new Activity("test");
        producer.Span = activity;
        producer.Span.Should().BeSameAs(activity);

        var scheduler = Substitute.For<IAmAMessageScheduler>();
        producer.Scheduler = scheduler;
        producer.Scheduler.Should().BeSameAs(scheduler);
    }

    [Fact]
    public async Task SendAsync_NullMessage_ThrowsArgumentNullException()
    {
        var outbox = Substitute.For<IOutbox>();
        var producer = new OutboxMessageProducer(outbox);

        var act = () => producer.SendAsync(null!);
        (await act.Should().ThrowAsync<ArgumentNullException>()).WithParameterName("message");
    }

    [Fact]
    public async Task SendAsync_WithTransactionContext_StoresInOutbox()
    {
        var outbox = Substitute.For<IOutbox>();
        var txContext = Substitute.For<IOutboxTransactionContext>();
        var producer = new OutboxMessageProducer(outbox, txContext);
        using var cts = new CancellationTokenSource();

        var payloadBytes = Encoding.UTF8.GetBytes("{\"id\":\"brighter-1\"}");
        var message = new Message(
            new MessageHeader(new Id(Guid.NewGuid().ToString()), new RoutingKey("test.topic"), MessageType.MT_EVENT),
            new MessageBody(payloadBytes));

        await producer.SendAsync(message, cts.Token);

        await outbox.Received(1).StoreAsync(payloadBytes, txContext, cts.Token);
    }

    [Fact]
    public async Task SendAsync_WithNullOrEmptyBody_DoesNotStoreInOutbox()
    {
        var outbox = Substitute.For<IOutbox>();
        var txContext = Substitute.For<IOutboxTransactionContext>();
        var producer = new OutboxMessageProducer(outbox, txContext);

        var emptyMessage = new Message(
            new MessageHeader(new Id(Guid.NewGuid().ToString()), new RoutingKey("test.topic"), MessageType.MT_EVENT),
            new MessageBody(Array.Empty<byte>()));

        await producer.SendAsync(emptyMessage, CancellationToken.None);

        var nullBodyMessage = new Message(
            new MessageHeader(new Id(Guid.NewGuid().ToString()), new RoutingKey("test.topic"), MessageType.MT_EVENT),
            null!);

        await producer.SendAsync(nullBodyMessage, CancellationToken.None);

        await outbox.DidNotReceive().StoreAsync(Arg.Any<object>(), Arg.Any<IOutboxTransactionContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_WithoutTransactionContext_DoesNotStoreInOutbox()
    {
        var outbox = Substitute.For<IOutbox>();
        var producer = new OutboxMessageProducer(outbox, transactionContext: null);

        var payloadBytes = Encoding.UTF8.GetBytes("{\"id\":\"brighter-1\"}");
        var message = new Message(
            new MessageHeader(new Id(Guid.NewGuid().ToString()), new RoutingKey("test.topic"), MessageType.MT_EVENT),
            new MessageBody(payloadBytes));

        await producer.SendAsync(message, CancellationToken.None);

        await outbox.DidNotReceive().StoreAsync(Arg.Any<object>(), Arg.Any<IOutboxTransactionContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendWithDelayAsync_NullMessage_ThrowsArgumentNullException()
    {
        var outbox = Substitute.For<IOutbox>();
        var producer = new OutboxMessageProducer(outbox);

        var act = () => producer.SendWithDelayAsync(null!);
        (await act.Should().ThrowAsync<ArgumentNullException>()).WithParameterName("message");
    }

    [Fact]
    public async Task SendWithDelayAsync_WithDelayAndTransaction_StoresWithDeliverAt()
    {
        var outbox = Substitute.For<IOutbox>();
        var txContext = Substitute.For<IOutboxTransactionContext>();
        var producer = new OutboxMessageProducer(outbox, txContext);

        var payloadBytes = Encoding.UTF8.GetBytes("{\"id\":\"brighter-delayed\"}");
        var message = new Message(
            new MessageHeader(new Id(Guid.NewGuid().ToString()), new RoutingKey("test.topic"), MessageType.MT_EVENT),
            new MessageBody(payloadBytes));

        using var cts = new CancellationTokenSource();
        var delay = TimeSpan.FromMinutes(5);

        await producer.SendWithDelayAsync(message, delay, cts.Token);

        await outbox.Received(1).StoreAsync(
            payloadBytes,
            txContext,
            Arg.Any<OutboxMessageMetadata>(),
            Arg.Is<DateTimeOffset?>(d => d.HasValue),
            cts.Token);
    }

    [Fact]
    public async Task SendWithDelayAsync_WithoutDelayAndTransaction_StoresWithoutDeliverAt()
    {
        var outbox = Substitute.For<IOutbox>();
        var txContext = Substitute.For<IOutboxTransactionContext>();
        var producer = new OutboxMessageProducer(outbox, txContext);

        var payloadBytes = Encoding.UTF8.GetBytes("{\"id\":\"brighter-nodelay\"}");
        var message = new Message(
            new MessageHeader(new Id(Guid.NewGuid().ToString()), new RoutingKey("test.topic"), MessageType.MT_EVENT),
            new MessageBody(payloadBytes));

        using var cts = new CancellationTokenSource();

        await producer.SendWithDelayAsync(message, null, cts.Token);

        await outbox.Received(1).StoreAsync(payloadBytes, txContext, cts.Token);
    }

    [Fact]
    public async Task SendWithDelayAsync_WithZeroDelay_StoresWithoutDeliverAt()
    {
        var outbox = Substitute.For<IOutbox>();
        var txContext = Substitute.For<IOutboxTransactionContext>();
        var producer = new OutboxMessageProducer(outbox, txContext);

        var payloadBytes = Encoding.UTF8.GetBytes("{\"id\":\"brighter-zerodelay\"}");
        var message = new Message(
            new MessageHeader(new Id(Guid.NewGuid().ToString()), new RoutingKey("test.topic"), MessageType.MT_EVENT),
            new MessageBody(payloadBytes));

        using var cts = new CancellationTokenSource();

        await producer.SendWithDelayAsync(message, TimeSpan.Zero, cts.Token);

        await outbox.Received(1).StoreAsync(payloadBytes, txContext, cts.Token);
    }

    [Fact]
    public async Task SendWithDelayAsync_WithoutTransaction_DoesNotStore()
    {
        var outbox = Substitute.For<IOutbox>();
        var producer = new OutboxMessageProducer(outbox, transactionContext: null);

        var payloadBytes = Encoding.UTF8.GetBytes("{\"id\":\"brighter-1\"}");
        var message = new Message(
            new MessageHeader(new Id(Guid.NewGuid().ToString()), new RoutingKey("test.topic"), MessageType.MT_EVENT),
            new MessageBody(payloadBytes));

        await producer.SendWithDelayAsync(message, TimeSpan.FromSeconds(10), CancellationToken.None);

        await outbox.DidNotReceive().StoreAsync(Arg.Any<object>(), Arg.Any<IOutboxTransactionContext>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendWithDelayAsync_WithNullOrEmptyBody_DoesNotStore()
    {
        var outbox = Substitute.For<IOutbox>();
        var txContext = Substitute.For<IOutboxTransactionContext>();
        var producer = new OutboxMessageProducer(outbox, txContext);

        var emptyMessage = new Message(
            new MessageHeader(new Id(Guid.NewGuid().ToString()), new RoutingKey("test.topic"), MessageType.MT_EVENT),
            new MessageBody(Array.Empty<byte>()));

        await producer.SendWithDelayAsync(emptyMessage, TimeSpan.FromSeconds(5), CancellationToken.None);

        var nullBodyMessage = new Message(
            new MessageHeader(new Id(Guid.NewGuid().ToString()), new RoutingKey("test.topic"), MessageType.MT_EVENT),
            null!);

        await producer.SendWithDelayAsync(nullBodyMessage, TimeSpan.FromSeconds(5), CancellationToken.None);

        await outbox.DidNotReceive().StoreAsync(Arg.Any<object>(), Arg.Any<IOutboxTransactionContext>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dispose_And_DisposeAsync_ExecuteCleanly()
    {
        var outbox = Substitute.For<IOutbox>();
        var producer = new OutboxMessageProducer(outbox);

        producer.Dispose();
        await producer.DisposeAsync();
    }

    [Fact]
    public void AddOutboxBrighterProducer_NullServices_ThrowsArgumentNullException()
    {
        Action act = () => BrighterOutboxServiceCollectionExtensions.AddOutboxBrighterProducer(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddOutboxBrighterProducer_RegistersService()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => Substitute.For<IOutbox>());
        services.AddOutboxBrighterProducer();

        var sp = services.BuildServiceProvider();
        var producer = sp.GetService<IAmAMessageProducerAsync>();

        producer.Should().NotBeNull();
        producer.Should().BeOfType<OutboxMessageProducer>();
    }
}

