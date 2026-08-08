using System;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox.Testing;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Testing;

public class FakeIdempotencyRepositoryTests
{
    private readonly FakeIdempotencyRepository _repo = new();

    [Fact]
    public async Task TryInsertAsync_InsertsRecord()
    {
        var record = new IdempotencyRecord("msg1", "cons1", DateTimeOffset.UtcNow);
        
        var result1 = await _repo.TryInsertAsync(record);
        result1.Should().BeTrue();
        _repo.Count.Should().Be(1);
        _repo.Records.Count.Should().Be(1);
        _repo.WasProcessed("msg1", "cons1").Should().BeTrue();

        var result2 = await _repo.TryInsertAsync(record);
        result2.Should().BeFalse();
        _repo.Count.Should().Be(1);
    }

    [Fact]
    public async Task PurgeExpiredRecordsAsync_RemovesOldRecords()
    {
        var now = DateTimeOffset.UtcNow;
        var recordOld = new IdempotencyRecord("msg1", "cons1", now.AddDays(-2));
        var recordNew = new IdempotencyRecord("msg2", "cons2", now);

        await _repo.TryInsertAsync(recordOld);
        await _repo.TryInsertAsync(recordNew);

        _repo.Count.Should().Be(2);

        await _repo.PurgeExpiredRecordsAsync(now.AddDays(-1));

        _repo.Count.Should().Be(1);
        _repo.WasProcessed("msg1", "cons1").Should().BeFalse();
        _repo.WasProcessed("msg2", "cons2").Should().BeTrue();
    }

    [Fact]
    public async Task Clear_RemovesAllRecords()
    {
        var record = new IdempotencyRecord("msg1", "cons1", DateTimeOffset.UtcNow);
        await _repo.TryInsertAsync(record);
        _repo.Count.Should().Be(1);

        _repo.Clear();

        _repo.Count.Should().Be(0);
        _repo.WasProcessed("msg1", "cons1").Should().BeFalse();
    }
}
