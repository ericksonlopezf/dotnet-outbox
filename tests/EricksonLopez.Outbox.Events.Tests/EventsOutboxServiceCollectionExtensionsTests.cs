// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using EricksonLopez.Events.Contracts;
using EricksonLopez.Outbox.Events;
using EricksonLopez.Outbox.Persistence;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Events.Tests;

[Trait("Category", "Unit")]
public sealed class EventsOutboxServiceCollectionExtensionsTests
{
    private sealed class CustomTestTransactionProvider : IOutboxTransactionProvider
    {
        public IOutboxTransactionContext? CurrentTransaction => null;
    }

    [Fact]
    public void AddOutboxEventPublisher_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;
        Action act = () => services.AddOutboxEventPublisher();
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddOutboxEventPublisher_Generic_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;
        Action act = () => services.AddOutboxEventPublisher<CustomTestTransactionProvider>();
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddOutboxEventPublisher_RegistersNullTransactionProviderAndEventPublisher()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IOutbox>());
        services.AddOutboxEventPublisher();

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var txProvider = scope.ServiceProvider.GetService<IOutboxTransactionProvider>();
        txProvider.Should().NotBeNull();
        txProvider.Should().BeOfType<NullOutboxTransactionProvider>();

        var publisher = scope.ServiceProvider.GetService<IEventPublisher>();
        publisher.Should().NotBeNull();
        publisher.Should().BeOfType<OutboxEventPublisher>();
    }

    [Fact]
    public void AddOutboxEventPublisher_Generic_RegistersCustomTransactionProviderAndEventPublisher()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IOutbox>());
        services.AddOutboxEventPublisher<CustomTestTransactionProvider>();

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var txProvider = scope.ServiceProvider.GetService<IOutboxTransactionProvider>();
        txProvider.Should().NotBeNull();
        txProvider.Should().BeOfType<CustomTestTransactionProvider>();

        var publisher = scope.ServiceProvider.GetService<IEventPublisher>();
        publisher.Should().NotBeNull();
        publisher.Should().BeOfType<OutboxEventPublisher>();
    }
}
