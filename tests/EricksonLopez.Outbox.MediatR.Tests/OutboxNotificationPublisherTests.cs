// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Contracts;
using EricksonLopez.Outbox.MediatR;
using EricksonLopez.Outbox.Persistence;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.MediatR.Tests;

public class OutboxNotificationPublisherTests
{
    [OutboxMessage("test.order.created")]
    public sealed record OrderCreatedNotification(Guid OrderId) : INotification;

    public sealed record PlainNotification(string Message) : INotification;

    [Fact]
    public void Constructor_NullOutbox_ThrowsArgumentNullException()
    {
        var act = () => new OutboxNotificationPublisher(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("outbox");
    }

    [Fact]
    public async Task Publish_NullParameters_ThrowsArgumentNullException()
    {
        var outbox = Substitute.For<IOutbox>();
        var publisher = new OutboxNotificationPublisher(outbox);

        var act1 = () => publisher.Publish(null!, new PlainNotification("test"), CancellationToken.None);
        (await act1.Should().ThrowAsync<ArgumentNullException>()).WithParameterName("handlerExecutors");

        var executors = new List<NotificationHandlerExecutor>();
        var act2 = () => publisher.Publish(executors, null!, CancellationToken.None);
        (await act2.Should().ThrowAsync<ArgumentNullException>()).WithParameterName("notification");
    }

    [Fact]
    public async Task Publish_OutboxMessageWithTransaction_StoresAndDispatchesToHandlers()
    {
        var outbox = Substitute.For<IOutbox>();
        var txContext = Substitute.For<IOutboxTransactionContext>();
        var publisher = new OutboxNotificationPublisher(outbox, txContext);
        using var cts = new CancellationTokenSource();

        var notification = new OrderCreatedNotification(Guid.NewGuid());
        bool handlerCalled = false;

        var handler = new NotificationHandlerExecutor(
            new object(),
            (notif, ct) =>
            {
                handlerCalled = true;
                return Task.CompletedTask;
            });

        var executors = new List<NotificationHandlerExecutor> { handler };

        await publisher.Publish(executors, notification, cts.Token);

        handlerCalled.Should().BeTrue();
        await outbox.Received(1).StoreAsync(notification, txContext, cts.Token);
    }

    [Fact]
    public async Task Publish_PlainNotificationWithoutOutboxAttr_DoesNotStoreInOutbox()
    {
        var outbox = Substitute.For<IOutbox>();
        var txContext = Substitute.For<IOutboxTransactionContext>();
        var publisher = new OutboxNotificationPublisher(outbox, txContext);

        var notification = new PlainNotification("plain message");
        bool handlerCalled = false;

        var handler = new NotificationHandlerExecutor(
            new object(),
            (notif, ct) =>
            {
                handlerCalled = true;
                return Task.CompletedTask;
            });

        var executors = new List<NotificationHandlerExecutor> { handler };

        await publisher.Publish(executors, notification, CancellationToken.None);

        handlerCalled.Should().BeTrue();
        await outbox.DidNotReceive().StoreAsync(Arg.Any<object>(), Arg.Any<IOutboxTransactionContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Publish_OutboxNotificationWithoutTransaction_DispatchesToHandlersWithoutStoring()
    {
        var outbox = Substitute.For<IOutbox>();
        var publisher = new OutboxNotificationPublisher(outbox, transactionContext: null);

        var notification = new OrderCreatedNotification(Guid.NewGuid());
        bool handlerCalled = false;

        var handler = new NotificationHandlerExecutor(
            new object(),
            (notif, ct) =>
            {
                handlerCalled = true;
                return Task.CompletedTask;
            });

        var executors = new List<NotificationHandlerExecutor> { handler };

        await publisher.Publish(executors, notification, CancellationToken.None);

        handlerCalled.Should().BeTrue();
        await outbox.DidNotReceive().StoreAsync(Arg.Any<object>(), Arg.Any<IOutboxTransactionContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AddOutboxMediatRPublisher_NullServices_ThrowsArgumentNullException()
    {
        Action act = () => OutboxMediatRServiceCollectionExtensions.AddOutboxMediatRPublisher(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddOutboxMediatRPublisher_RegistersServiceCorrectly()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => Substitute.For<IOutbox>());
        var existingPublisher = Substitute.For<INotificationPublisher>();
        services.AddSingleton(existingPublisher);

        services.AddOutboxMediatRPublisher();

        var sp = services.BuildServiceProvider();
        var publishers = sp.GetServices<INotificationPublisher>().ToList();

        publishers.Should().HaveCount(1);
        publishers[0].Should().BeOfType<OutboxNotificationPublisher>();
        publishers[0].Should().NotBeSameAs(existingPublisher);
    }
}



