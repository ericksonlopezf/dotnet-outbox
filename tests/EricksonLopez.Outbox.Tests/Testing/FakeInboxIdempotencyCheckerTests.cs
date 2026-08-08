using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Testing;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Testing;

public class FakeInboxIdempotencyCheckerTests
{
    private readonly FakeIdempotencyRepository _repo = new();
    private readonly FakeInboxIdempotencyChecker _checker;

    public FakeInboxIdempotencyCheckerTests()
    {
        _checker = new FakeInboxIdempotencyChecker(_repo);
    }

    [Fact]
    public async Task ShouldProcessAsync_WhenNotProcessed_ReturnsTrueAndRecords()
    {
        var tx = Substitute.For<IOutboxTransactionContext>();
        
        var result1 = await _checker.ShouldProcessAsync("msg1", "cons1", tx, CancellationToken.None);
        result1.Should().BeTrue();

        var result2 = await _checker.ShouldProcessAsync("msg1", "cons1", tx, CancellationToken.None);
        result2.Should().BeFalse(); // already processed
    }

    [Fact]
    public async Task ShouldSkipAsync_WhenNotProcessed_ReturnsFalseAndRecords()
    {
        var tx = Substitute.For<IOutboxTransactionContext>();
        var msgId = Guid.NewGuid();

        // ISSUE-C1: consumerId parameter is now 3rd (defaults to OutboxConstants.DispatcherConsumerId);
        // explicitly name cancellationToken to avoid positional ambiguity.
        var result1 = await _checker.ShouldSkipAsync(msgId, tx, cancellationToken: CancellationToken.None);
        result1.Should().BeFalse(); // not skipped (not processed before)

        var result2 = await _checker.ShouldSkipAsync(msgId, tx, cancellationToken: CancellationToken.None);
        result2.Should().BeTrue(); // should skip because it was recorded in result1
    }
}
