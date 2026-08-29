// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Mediator;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Contracts;
using EricksonLopez.Outbox.Persistence;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Mediator.Tests;

public sealed class OutboxNotificationHandlerTests
{
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly IOutboxTransactionContext _txContext = Substitute.For<IOutboxTransactionContext>();

    [OutboxMessage("OutboxSampleNotification")]
    public sealed record OutboxSampleNotification(string Name) : INotification;

    public sealed record NonOutboxSampleNotification(string Name) : INotification;

    [Fact]
    public void Constructor_WhenOutboxNull_ThrowsArgumentNullException()
    {
        var act = () => new OutboxNotificationHandler<OutboxSampleNotification>(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("outbox");
    }

    [Fact]
    public async Task Handle_WhenNotificationNull_ThrowsArgumentNullException()
    {
        var sut = new OutboxNotificationHandler<OutboxSampleNotification>(_outbox, _txContext);
        Func<Task> act = async () => await sut.Handle(null!, CancellationToken.None);
        (await act.Should().ThrowExactlyAsync<ArgumentNullException>()).WithParameterName("notification");
    }

    [Fact]
    public async Task Handle_WhenNotificationHasOutboxAttributeAndTransactionActive_StoresInOutbox()
    {
        var sut = new OutboxNotificationHandler<OutboxSampleNotification>(_outbox, _txContext);
        var notification = new OutboxSampleNotification("UserRegistered");
        using var cts = new CancellationTokenSource();

        await sut.Handle(notification, cts.Token);

        await _outbox.Received(1).StoreAsync(notification, _txContext, cts.Token);
    }

    [Fact]
    public async Task Handle_WhenNotificationHasOutboxAttributeButTransactionNull_DoesNotStoreInOutbox()
    {
        var sut = new OutboxNotificationHandler<OutboxSampleNotification>(_outbox, transactionContext: null);
        var notification = new OutboxSampleNotification("UserRegistered");

        await sut.Handle(notification, CancellationToken.None);

        await _outbox.DidNotReceive().StoreAsync(Arg.Any<object>(), Arg.Any<IOutboxTransactionContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenNotificationHasNoOutboxAttribute_DoesNotStoreInOutbox()
    {
        var sut = new OutboxNotificationHandler<NonOutboxSampleNotification>(_outbox, _txContext);
        var notification = new NonOutboxSampleNotification("UserLoggedIn");

        await sut.Handle(notification, CancellationToken.None);

        await _outbox.DidNotReceive().StoreAsync(Arg.Any<object>(), Arg.Any<IOutboxTransactionContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AddOutboxNotificationHandler_WhenServicesNull_ThrowsArgumentNullException()
    {
        Action act = () => OutboxMediatorServiceCollectionExtensions.AddOutboxNotificationHandler<OutboxSampleNotification>(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddOutboxNotificationHandler_RegistersHandlerInServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_outbox);
        services.AddSingleton(_txContext);

        var returnedServices = services.AddOutboxNotificationHandler<OutboxSampleNotification>();
        returnedServices.Should().BeSameAs(services);

        var descriptor = System.Linq.Enumerable.FirstOrDefault(services, d => d.ServiceType == typeof(INotificationHandler<OutboxSampleNotification>));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Transient);
        descriptor.ImplementationType.Should().Be<OutboxNotificationHandler<OutboxSampleNotification>>();

        var provider = services.BuildServiceProvider();
        var handler = provider.GetService<INotificationHandler<OutboxSampleNotification>>();

        handler.Should().NotBeNull();
        handler.Should().BeOfType<OutboxNotificationHandler<OutboxSampleNotification>>();
    }
}
