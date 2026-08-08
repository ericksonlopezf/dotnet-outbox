using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Testing;

namespace EricksonLopez.Outbox.Tests.Testing;

public class FakeDeadLetterRepositoryTests
{
    private readonly FakeDeadLetterRepository _sut = new();

    [Fact]
    public async Task Insert_And_Get_Works()
    {
        var msg = CreateMessage(DateTimeOffset.UtcNow);
        await _sut.InsertAsync(msg);
        
        _sut.Count.Should().Be(1);
        _sut.Messages.Should().ContainSingle().Which.Id.Should().Be(msg.Id);
    }

    [Fact]
    public async Task GetAsync_RespectsAfter()
    {
        var now = DateTimeOffset.UtcNow;
        var msg1 = CreateMessage(now.AddMinutes(-5));
        var msg2 = CreateMessage(now.AddMinutes(5));
        
        await _sut.InsertAsync(msg1);
        await _sut.InsertAsync(msg2);
        
        var result = await _sut.GetAsync(100, now);
        
        result.Should().ContainSingle().Which.Id.Should().Be(msg2.Id);
    }

    [Fact]
    public async Task GetAsync_OrdersByDeadLetteredAt()
    {
        var now = DateTimeOffset.UtcNow;
        var msg1 = CreateMessage(now.AddMinutes(5));
        var msg2 = CreateMessage(now.AddMinutes(-5));
        
        await _sut.InsertAsync(msg1);
        await _sut.InsertAsync(msg2);
        
        var result = await _sut.GetAsync(100);
        
        result.Should().HaveCount(2);
        result[0].Id.Should().Be(msg2.Id); // older first
        result[1].Id.Should().Be(msg1.Id);
    }

    [Fact]
    public async Task DeleteAsync_RemovesMessage()
    {
        var msg = CreateMessage(DateTimeOffset.UtcNow);
        await _sut.InsertAsync(msg);
        
        await _sut.DeleteAsync(msg.Id);
        
        _sut.Count.Should().Be(0);
    }

    [Fact]
    public async Task PurgeAsync_RemovesOlderThan()
    {
        var now = DateTimeOffset.UtcNow;
        var msg1 = CreateMessage(now.AddMinutes(-5));
        var msg2 = CreateMessage(now.AddMinutes(5));
        
        await _sut.InsertAsync(msg1);
        await _sut.InsertAsync(msg2);
        
        await _sut.PurgeAsync(now);
        
        _sut.Count.Should().Be(1);
        _sut.Messages.Single().Id.Should().Be(msg2.Id);
    }

    [Fact]
    public async Task Clear_EmptiesRepository()
    {
        await _sut.InsertAsync(CreateMessage(DateTimeOffset.UtcNow));
        _sut.Clear();
        _sut.Count.Should().Be(0);
    }

    private static DeadLetterMessage CreateMessage(DateTimeOffset deadLetteredAt)
    {
        return new DeadLetterMessage(
            Guid.NewGuid(), 
            Guid.NewGuid(), 
            "Type", 
            ReadOnlyMemory<byte>.Empty, 
            null, 
            null, 
            ReadOnlyMemory<byte>.Empty, 
            DateTimeOffset.UtcNow, 
            deadLetteredAt, 
            3, 
            "Error", 
            null);
    }
}
