// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Events.Contracts;
using EricksonLopez.Events.Identifiers;
using EricksonLopez.Inbox;
using EricksonLopez.Outbox.Inbox.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.Core;
using Xunit;
using EventId = EricksonLopez.Events.Identifiers.EventId;

namespace EricksonLopez.Outbox.Inbox.Events.Tests;

[Trait("Category", "Unit")]
public sealed class EventInboxServiceCollectionExtensionsTests
{
    public sealed record SampleOrderSubmitted(EventId Id, decimal Amount, DateTimeOffset OccurredAt) : IIntegrationEvent;

    public sealed class SampleOrderSubmittedHandler : IEventHandler<SampleOrderSubmitted>
    {
        public bool Handled { get; private set; }

        public ValueTask HandleAsync(SampleOrderSubmitted eventInstance, CancellationToken cancellationToken = default)
        {
            Handled = true;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public void AddIdempotentEventHandler_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;
        Action act = () => services.AddIdempotentEventHandler<SampleOrderSubmitted, SampleOrderSubmittedHandler>();
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public async Task AddIdempotentEventHandler_WithExplicitConsumerName_RegistersAndResolvesDecoratedHandler()
    {
        string? capturedConsumer = null;
        var inboxFilter = Substitute.For<IInboxConsumerFilter>();

        Func<CallInfo, ValueTask<bool>> callback = async callInfo =>
        {
            capturedConsumer = callInfo.ArgAt<string>(1);
            var handler = callInfo.Arg<Func<CancellationToken, ValueTask>>();
            var ct = callInfo.Arg<CancellationToken>();
            await handler(ct);
            return true;
        };

        inboxFilter.ExecuteIdempotentlyAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, ValueTask>>(),
            Arg.Any<CancellationToken>())
            .Returns(callback);

        var services = new ServiceCollection();
        services.AddSingleton(inboxFilter);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        var returnedServices = services.AddIdempotentEventHandler<SampleOrderSubmitted, SampleOrderSubmittedHandler>("CustomOrderConsumer");
        returnedServices.Should().BeSameAs(services);

        var handlerDescriptor = System.Linq.Enumerable.FirstOrDefault(services, d => d.ServiceType == typeof(SampleOrderSubmittedHandler));
        handlerDescriptor.Should().NotBeNull();
        handlerDescriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped);

        var eventHandlerDescriptor = System.Linq.Enumerable.FirstOrDefault(services, d => d.ServiceType == typeof(IEventHandler<SampleOrderSubmitted>));
        eventHandlerDescriptor.Should().NotBeNull();
        eventHandlerDescriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped);

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();

        var handler = scope.ServiceProvider.GetService<IEventHandler<SampleOrderSubmitted>>();
        handler.Should().NotBeNull();
        handler.Should().BeOfType<IdempotentEventHandler<SampleOrderSubmitted>>();

        var innerInstance = scope.ServiceProvider.GetService<SampleOrderSubmittedHandler>();
        innerInstance.Should().NotBeNull();

        var evt = new SampleOrderSubmitted(EventId.New(), 299.99m, DateTimeOffset.UtcNow);
        await handler!.HandleAsync(evt);

        innerInstance!.Handled.Should().BeTrue();
        capturedConsumer.Should().Be("CustomOrderConsumer");
    }

    [Fact]
    public async Task AddIdempotentEventHandler_WithoutConsumerName_UsesInnerHandlerTypeNameAsConsumer()
    {
        string? capturedConsumer = null;
        var inboxFilter = Substitute.For<IInboxConsumerFilter>();

        Func<CallInfo, ValueTask<bool>> callback = async callInfo =>
        {
            capturedConsumer = callInfo.ArgAt<string>(1);
            var handler = callInfo.Arg<Func<CancellationToken, ValueTask>>();
            var ct = callInfo.Arg<CancellationToken>();
            await handler(ct);
            return true;
        };

        inboxFilter.ExecuteIdempotentlyAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, ValueTask>>(),
            Arg.Any<CancellationToken>())
            .Returns(callback);

        var services = new ServiceCollection();
        services.AddSingleton(inboxFilter);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        var returnedServices = services.AddIdempotentEventHandler<SampleOrderSubmitted, SampleOrderSubmittedHandler>();
        returnedServices.Should().BeSameAs(services);

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();

        var handler = scope.ServiceProvider.GetService<IEventHandler<SampleOrderSubmitted>>();
        handler.Should().NotBeNull();
        handler.Should().BeOfType<IdempotentEventHandler<SampleOrderSubmitted>>();

        var innerInstance = scope.ServiceProvider.GetService<SampleOrderSubmittedHandler>();
        innerInstance.Should().NotBeNull();

        var evt = new SampleOrderSubmitted(EventId.New(), 299.99m, DateTimeOffset.UtcNow);
        await handler!.HandleAsync(evt);

        innerInstance!.Handled.Should().BeTrue();
        capturedConsumer.Should().Be(typeof(SampleOrderSubmittedHandler).FullName);
    }

    [Fact]
    public async Task AddIdempotentEventHandler_InjectsCustomLoggerFromServiceProvider()
    {
        var inboxFilter = Substitute.For<IInboxConsumerFilter>();
        inboxFilter.ExecuteIdempotentlyAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, ValueTask>>(),
            Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(false));

        var mockLogger = Substitute.For<ILogger<IdempotentEventHandler<SampleOrderSubmitted>>>();
        mockLogger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        var services = new ServiceCollection();
        services.AddSingleton(inboxFilter);
        services.AddSingleton(mockLogger);
        var returnedServices = services.AddIdempotentEventHandler<SampleOrderSubmitted, SampleOrderSubmittedHandler>("LoggedConsumer");
        returnedServices.Should().BeSameAs(services);

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();

        var handler = scope.ServiceProvider.GetRequiredService<IEventHandler<SampleOrderSubmitted>>();
        var evt = new SampleOrderSubmitted(EventId.New(), 99.99m, DateTimeOffset.UtcNow);

        await handler.HandleAsync(evt);

        var logCalls = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(mockLogger.ReceivedCalls(), c => c.GetMethodInfo().Name == "Log"));
        logCalls.Should().ContainSingle();
        var call = logCalls[0];
        call.GetArguments()[0].Should().Be(LogLevel.Debug);
        var logMessage = call.GetArguments()[2]?.ToString();
        logMessage.Should().NotBeNull();
        logMessage.Should().Contain("was skipped as a duplicate by consumer 'LoggedConsumer'");
    }
}
