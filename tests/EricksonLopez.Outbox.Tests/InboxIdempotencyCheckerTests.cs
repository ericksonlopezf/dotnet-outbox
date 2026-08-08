#pragma warning disable CA2012
using System;

using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Idempotency;
using EricksonLopez.Outbox.Persistence;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

public class InboxIdempotencyCheckerTests
{
    [Fact]
    public async Task ShouldProcessAsync_Should_Return_True_When_Inserted()
    {
        var store = Substitute.For<IIdempotencyRepository>();
        var middleware = new InboxIdempotencyChecker(store);
        var transaction = Substitute.For<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>();

        _ = store.TryInsertAsync(Arg.Any<IdempotencyRecord>(), transaction, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<bool>(true));

        var result = await middleware.ShouldProcessAsync("msg1", "consumer1", transaction);

        result.Should().BeTrue();
        await store.Received().TryInsertAsync(
            Arg.Is<IdempotencyRecord>(r => r.MessageId == "msg1" && r.ConsumerId == "consumer1"),
            transaction,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShouldProcessAsync_Should_Return_False_When_Not_Inserted()
    {
        var store = Substitute.For<IIdempotencyRepository>();
        var middleware = new InboxIdempotencyChecker(store);
        var transaction = Substitute.For<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>();

        _ = store.TryInsertAsync(Arg.Any<IdempotencyRecord>(), transaction, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<bool>(false));

        var result = await middleware.ShouldProcessAsync("msg1", "consumer1", transaction);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ShouldSkipAsync_Should_Return_False_When_Inserted()
    {
        var store = Substitute.For<IIdempotencyRepository>();
        var middleware = new InboxIdempotencyChecker(store);
        var id = Guid.NewGuid();

        _ = store.TryInsertAsync(Arg.Any<IdempotencyRecord>(), null, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<bool>(true));

        var result = await middleware.ShouldSkipAsync(id, null!);

        result.Should().BeFalse();
        // ISSUE-C1 FIX: Assert against OutboxConstants.DispatcherConsumerId constant
        // instead of the raw string, so the test reflects the intent and remains
        // in sync if the constant value ever changes.
        await store.Received().TryInsertAsync(
            Arg.Is<IdempotencyRecord>(r => r.MessageId == id.ToString() && r.ConsumerId == OutboxConstants.DispatcherConsumerId),
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShouldSkipAsync_Should_Return_True_When_Not_Inserted()
    {
        var store = Substitute.For<IIdempotencyRepository>();
        var middleware = new InboxIdempotencyChecker(store);
        var id = Guid.NewGuid();

        _ = store.TryInsertAsync(Arg.Any<IdempotencyRecord>(), null, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<bool>(false));

        var result = await middleware.ShouldSkipAsync(id, null!);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldSkipAsync_Should_Use_Custom_ConsumerId_When_Provided()
    {
        // ISSUE-C1: Verify that a caller can provide a custom consumerId to avoid
        // colliding with the dispatcher's own idempotency records.
        var store = Substitute.For<IIdempotencyRepository>();
        var middleware = new InboxIdempotencyChecker(store);
        var id = Guid.NewGuid();
        const string customConsumerId = "order-service.payment-handler";

        _ = store.TryInsertAsync(Arg.Any<IdempotencyRecord>(), null, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<bool>(false));

        await middleware.ShouldSkipAsync(id, null!, consumerId: customConsumerId);

        await store.Received().TryInsertAsync(
            Arg.Is<IdempotencyRecord>(r => r.ConsumerId == customConsumerId),
            null,
            Arg.Any<CancellationToken>());
    }
}


