// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Inbox;
using EricksonLopez.Outbox.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Inbox.Tests;

public class InboxConsumerFilterTests
{
    [Fact]
    public void Constructor_NullParameters_ThrowsArgumentNullException()
    {
        var repo = Substitute.For<IIdempotencyRepository>();
        var logger = NullLogger<InboxConsumerFilter>.Instance;

        Action act1 = () => _ = new InboxConsumerFilter(null!, logger);
        act1.Should().Throw<ArgumentNullException>().WithParameterName("idempotencyRepository");

        Action act2 = () => _ = new InboxConsumerFilter(repo, null!);
        act2.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task ExecuteIdempotentlyAsync_NullParameters_ThrowsArgumentNullException()
    {
        var repo = Substitute.For<IIdempotencyRepository>();
        var filter = new InboxConsumerFilter(repo, NullLogger<InboxConsumerFilter>.Instance);

        Func<Task> act1 = async () => await filter.ExecuteIdempotentlyAsync(null!, "consumer-1", _ => ValueTask.CompletedTask);
        await act1.Should().ThrowAsync<ArgumentNullException>().WithParameterName("messageId");

        Func<Task> act2 = async () => await filter.ExecuteIdempotentlyAsync("msg-1", null!, _ => ValueTask.CompletedTask);
        await act2.Should().ThrowAsync<ArgumentNullException>().WithParameterName("consumerName");

        Func<Task> act3 = async () => await filter.ExecuteIdempotentlyAsync("msg-1", "consumer-1", null!);
        await act3.Should().ThrowAsync<ArgumentNullException>().WithParameterName("handler");
    }

    [Fact]
    public async Task ExecuteIdempotentlyAsync_NewMessage_ExecutesHandlerAndReturnsTrue()
    {
        var repo = Substitute.For<IIdempotencyRepository>();
        repo.TryInsertAsync(Arg.Any<IdempotencyRecord>(), Arg.Any<IOutboxTransactionContext?>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));

        var filter = new InboxConsumerFilter(repo, NullLogger<InboxConsumerFilter>.Instance);

        var handlerExecuted = false;
        var result = await filter.ExecuteIdempotentlyAsync(
            "msg-100",
            "order-consumer",
            _ =>
            {
                handlerExecuted = true;
                return ValueTask.CompletedTask;
            });

        result.Should().BeTrue();
        handlerExecuted.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteIdempotentlyAsync_DuplicateMessage_LogsAndSkipsHandler()
    {
        var repo = Substitute.For<IIdempotencyRepository>();
        repo.TryInsertAsync(Arg.Any<IdempotencyRecord>(), Arg.Any<IOutboxTransactionContext?>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(false));

        var logger = Substitute.For<ILogger<InboxConsumerFilter>>();
        var filter = new InboxConsumerFilter(repo, logger);

        var handlerExecuted = false;
        var result = await filter.ExecuteIdempotentlyAsync(
            "msg-100",
            "order-consumer",
            _ =>
            {
                handlerExecuted = true;
                return ValueTask.CompletedTask;
            });

        result.Should().BeFalse();
        handlerExecuted.Should().BeFalse();

        logger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Duplicate message detected: Id=msg-100, Consumer=order-consumer")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task ExecuteIdempotentlyAsync_WhenHandlerThrows_PropagatesException()
    {
        var repo = Substitute.For<IIdempotencyRepository>();
        repo.TryInsertAsync(Arg.Any<IdempotencyRecord>(), Arg.Any<IOutboxTransactionContext?>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));

        var filter = new InboxConsumerFilter(repo, NullLogger<InboxConsumerFilter>.Instance);
        var expectedEx = new InvalidOperationException("Business logic failed");

        Func<Task> act = async () => await filter.ExecuteIdempotentlyAsync(
            "msg-error",
            "order-consumer",
            _ => throw expectedEx);

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Should().BeSameAs(expectedEx);
    }

    [Fact]
    public async Task ExecuteIdempotentlyAsync_WithTransactionContext_PassesTransactionToRepository()
    {
        var repo = Substitute.For<IIdempotencyRepository>();
        var tx = Substitute.For<IOutboxTransactionContext>();
        repo.TryInsertAsync(Arg.Any<IdempotencyRecord>(), tx, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));

        var filter = new InboxConsumerFilter(repo, NullLogger<InboxConsumerFilter>.Instance);

        var result = await filter.ExecuteIdempotentlyAsync(
            "msg-tx",
            "order-consumer",
            _ => ValueTask.CompletedTask,
            transaction: tx);

        result.Should().BeTrue();
        _ = repo.Received(1).TryInsertAsync(
            Arg.Is<IdempotencyRecord>(r => r.MessageId == "msg-tx" && r.ConsumerId == "order-consumer"),
            tx,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteIdempotentlyAsync_WithCancellationToken_PropagatesCancellationToken()
    {
        var repo = Substitute.For<IIdempotencyRepository>();
        using var cts = new CancellationTokenSource();
        repo.TryInsertAsync(Arg.Any<IdempotencyRecord>(), Arg.Any<IOutboxTransactionContext?>(), cts.Token)
            .Returns(ValueTask.FromResult(true));

        var filter = new InboxConsumerFilter(repo, NullLogger<InboxConsumerFilter>.Instance);

        CancellationToken receivedToken = default;
        var result = await filter.ExecuteIdempotentlyAsync(
            "msg-token",
            "order-consumer",
            ct =>
            {
                receivedToken = ct;
                return ValueTask.CompletedTask;
            },
            cancellationToken: cts.Token);

        result.Should().BeTrue();
        receivedToken.Should().Be(cts.Token);
        _ = repo.Received(1).TryInsertAsync(
            Arg.Any<IdempotencyRecord>(),
            Arg.Any<IOutboxTransactionContext?>(),
            cts.Token);
    }

    [Fact]
    public async Task ExplicitInterfaceExecuteIdempotentlyAsync_Invokes5ParamOverloadWithNullTransaction()
    {
        var repo = Substitute.For<IIdempotencyRepository>();
        using var cts = new CancellationTokenSource();
        repo.TryInsertAsync(Arg.Any<IdempotencyRecord>(), null, cts.Token)
            .Returns(ValueTask.FromResult(true));

        var filter = new InboxConsumerFilter(repo, NullLogger<InboxConsumerFilter>.Instance);
        var explicitFilter = (EricksonLopez.Inbox.IInboxConsumerFilter)filter;

        var executed = false;
        var result = await explicitFilter.ExecuteIdempotentlyAsync(
            "msg-explicit",
            "consumer-explicit",
            ct =>
            {
                executed = (ct == cts.Token);
                return ValueTask.CompletedTask;
            },
            cts.Token);

        result.Should().BeTrue();
        executed.Should().BeTrue();
        _ = repo.Received(1).TryInsertAsync(
            Arg.Is<IdempotencyRecord>(r => r.MessageId == "msg-explicit" && r.ConsumerId == "consumer-explicit"),
            null,
            cts.Token);
    }

    [Fact]
    public void AddInboxDeduplication_RegistersServiceInContainer_AndReturnsServices()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => Substitute.For<IIdempotencyRepository>());
        services.AddLogging();
        var result = services.AddInboxDeduplication();
        result.Should().BeSameAs(services);

        var sp = services.BuildServiceProvider();
        var filter = sp.GetService<IInboxConsumerFilter>();

        filter.Should().NotBeNull();
        filter.Should().BeOfType<InboxConsumerFilter>();
    }

    [Fact]
    public void AddInboxDeduplication_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;
        Action act = () => services.AddInboxDeduplication();
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }
}
