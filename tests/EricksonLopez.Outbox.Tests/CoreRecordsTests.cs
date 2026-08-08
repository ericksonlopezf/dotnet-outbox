using System;
using System.Text;
using AwesomeAssertions;
using Xunit;
using EricksonLopez.Outbox;

namespace EricksonLopez.Outbox.Tests.Core;

public class CoreRecordsTests
{
    [Fact]
    public void DeadLetterMessage_FromOutboxMessage_Should_Map_Fields_Correctly()
    {
        var original = new OutboxMessage(
            Id: Guid.NewGuid(),
            MessageType: "TestMessage",
            Payload: Encoding.UTF8.GetBytes("test"),
            CorrelationId: "corr-1",
            CausationId: "caus-1",
            Headers: System.Text.Encoding.UTF8.GetBytes("{}"),
            CreatedAt: DateTimeOffset.UtcNow,
            ProcessedAt: null,
            DeliverAt: null,
            Status: OutboxMessageStatus.Failed,
            RetryCount: 0,
            Error: null
        );

        var deadLetter = DeadLetterMessage.FromOutboxMessage(original, 5, "Exhausted", "Some error");

        deadLetter.Id.Should().NotBeEmpty();
        deadLetter.Id.Should().NotBe(original.Id);
        deadLetter.OriginalMessageId.Should().Be(original.Id);
        deadLetter.MessageType.Should().Be(original.MessageType);
        deadLetter.Payload.ToArray().Should().BeEquivalentTo(original.Payload.ToArray());
        deadLetter.CorrelationId.Should().Be(original.CorrelationId);
        deadLetter.CausationId.Should().Be(original.CausationId);
        deadLetter.Headers.ToArray().Should().BeEquivalentTo(original.Headers.ToArray());
        deadLetter.CreatedAt.Should().Be(original.CreatedAt);
        deadLetter.DeadLetteredAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        deadLetter.RetryCount.Should().Be(5);
        deadLetter.Reason.Should().Be("Exhausted");
        deadLetter.LastError.Should().Be("Some error");
    }

    [Fact]
    public void FailedMessage_FromOutboxMessage_Should_Map_Fields_Correctly_With_RetryAfter()
    {
        var original = new OutboxMessage(
            Id: Guid.NewGuid(),
            MessageType: "TestMessage",
            Payload: Encoding.UTF8.GetBytes("test"),
            CorrelationId: null,
            CausationId: null,
            Headers: System.Text.Encoding.UTF8.GetBytes("{}"),
            CreatedAt: DateTimeOffset.UtcNow,
            ProcessedAt: null,
            DeliverAt: null,
            Status: OutboxMessageStatus.InFlight,
            RetryCount: 0,
            Error: null
        );

        var failed = FailedMessage.FromOutboxMessage(original, 2, "Transient Error", TimeSpan.FromMinutes(5));

        failed.Id.Should().NotBeEmpty();
        failed.OriginalMessageId.Should().Be(original.Id);
        failed.MessageType.Should().Be(original.MessageType);
        failed.Payload.ToArray().Should().BeEquivalentTo(original.Payload.ToArray());
        failed.CorrelationId.Should().BeNull();
        failed.CausationId.Should().BeNull();
        failed.Headers.ToArray().Should().BeEquivalentTo(original.Headers.ToArray());
        failed.CreatedAt.Should().Be(original.CreatedAt);
        failed.FailedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        failed.Error.Should().Be("Transient Error");
        failed.RetryCount.Should().Be(2);
        failed.NextRetryAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(5), TimeSpan.FromSeconds(1));
    }
    
    [Fact]
    public void FailedMessage_FromOutboxMessage_Should_Map_Fields_Correctly_Without_RetryAfter()
    {
        var original = new OutboxMessage(
            Id: Guid.NewGuid(),
            MessageType: "TestMessage",
            Payload: Encoding.UTF8.GetBytes("test"),
            CorrelationId: null,
            CausationId: null,
            Headers: System.Text.Encoding.UTF8.GetBytes("{}"),
            CreatedAt: DateTimeOffset.UtcNow,
            ProcessedAt: null,
            DeliverAt: null,
            Status: OutboxMessageStatus.InFlight,
            RetryCount: 0,
            Error: null
        );

        var failed = FailedMessage.FromOutboxMessage(original, 2, "Transient Error", null);

        failed.NextRetryAt.Should().BeNull();
    }
    
    [Fact]
    public void MessageMetadata_GetValue_Should_Return_Correct_Value_When_Exists()
    {
        var entries = new[]
        {
            new MetadataEntry("ZKey", "ZValue"),
            new MetadataEntry("AKey", "AValue"),
            new MetadataEntry("MKey", "MValue")
        };
        
        var metadata = new MessageMetadata("corr-1", "caus-1", "type-1", entries);
        
        metadata.CorrelationId.Should().Be("corr-1");
        metadata.CausationId.Should().Be("caus-1");
        metadata.MessageType.Should().Be("type-1");
        
        metadata.GetValue("AKey").Should().Be("AValue");
        metadata.GetValue("MKey").Should().Be("MValue");
        metadata.GetValue("ZKey").Should().Be("ZValue");
    }
    
    [Fact]
    public void MessageMetadata_GetValue_Should_Return_Null_When_Not_Exists_Or_Empty()
    {
        var metadata = new MessageMetadata("corr-1", "caus-1", "type-1");
        
        metadata.GetValue("AnyKey").Should().BeNull();
        metadata.Entries.Length.Should().Be(0);
        
        var metadataWithEntries = new MessageMetadata("corr-1", "caus-1", "type-1", new[] { new MetadataEntry("A", "B") });
        metadataWithEntries.GetValue("MissingKey").Should().BeNull();
    }

    [Fact]
    public void CoreRecords_Can_Be_Instantiated()
    {
        var idempotency = new IdempotencyRecord("msg-1", "consumer-1", DateTimeOffset.UtcNow);
        idempotency.MessageId.Should().Be("msg-1");
        idempotency.ConsumerId.Should().Be("consumer-1");
        
        var inboxMsg = new InboxMessage(Guid.NewGuid(), "type-1", default, null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, 0, null);
        inboxMsg.MessageType.Should().Be("type-1");
        inboxMsg.Status.Should().Be(0);

        var lease = new Lease("node-1", "o1", DateTimeOffset.UtcNow);
        lease.OwnerId.Should().Be("o1");

        var lockObj = new EricksonLopez.Outbox.Lock("lock-key", "owner-1");
        lockObj.ResourceId.Should().Be("lock-key");
        
        var publisher = Publisher.Create("pub-name");
        publisher.Name.Should().Be("pub-name");
    }

    [Fact]
    public void CoreRecords_Equality_And_HashCode_Coverage()
    {
        var headers = System.Text.Encoding.UTF8.GetBytes("{}");
        var msg1 = new OutboxMessage(Guid.Parse("00000000-0000-0000-0000-000000000001"), "T1", default, null, null, headers, DateTimeOffset.MinValue, null, null, 0, 0, null);
        var msg2 = new OutboxMessage(Guid.Parse("00000000-0000-0000-0000-000000000001"), "T1", default, null, null, headers, DateTimeOffset.MinValue, null, null, 0, 0, null);
        var msg3 = new OutboxMessage(Guid.Parse("00000000-0000-0000-0000-000000000002"), "T2", default, null, null, headers, DateTimeOffset.MinValue, null, null, 0, 0, null);

        // Equals
        msg1.Equals(msg2).Should().BeTrue();
        msg1.Equals((object)msg2).Should().BeTrue();
        msg1.Equals(msg3).Should().BeFalse();
        msg1.Equals((object)msg3).Should().BeFalse();

        // Operators
        (msg1 == msg2).Should().BeTrue();
        (msg1 != msg2).Should().BeFalse();
        (msg1 == msg3).Should().BeFalse();
        (msg1 != msg3).Should().BeTrue();

        // HashCode
        msg1.GetHashCode().Should().Be(msg2.GetHashCode());

        // ToString
        msg1.ToString().Should().NotBeNullOrWhiteSpace();

        // Do the same for InboxMessage
        var in1 = new InboxMessage(msg1.Id, "T", default, null, null, headers, DateTimeOffset.MinValue, null, 0, null);
        var in2 = new InboxMessage(msg1.Id, "T", default, null, null, headers, DateTimeOffset.MinValue, null, 0, null);
        (in1 == in2).Should().BeTrue();
        (in1 != in2).Should().BeFalse();
        in1.Equals(in2).Should().BeTrue();
        in1.Equals((object)in2).Should().BeTrue();
        in1.GetHashCode().Should().Be(in2.GetHashCode());
        in1.ToString().Should().NotBeNullOrWhiteSpace();
        
        // DeadLetterMessage
        var d1 = new DeadLetterMessage(msg1.Id, msg1.Id, "T", default, null, null, headers, DateTimeOffset.MinValue, DateTimeOffset.MinValue, 0, "R", null);
        var d2 = new DeadLetterMessage(msg1.Id, msg1.Id, "T", default, null, null, headers, DateTimeOffset.MinValue, DateTimeOffset.MinValue, 0, "R", null);
        (d1 == d2).Should().BeTrue();
        (d1 != d2).Should().BeFalse();
        d1.Equals(d2).Should().BeTrue();
        d1.Equals((object)d2).Should().BeTrue();
        d1.GetHashCode().Should().Be(d2.GetHashCode());
        d1.ToString().Should().NotBeNullOrWhiteSpace();
        
        // FailedMessage
        var f1 = new FailedMessage(msg1.Id, msg1.Id, "T", default, null, null, headers, DateTimeOffset.MinValue, DateTimeOffset.MinValue, null, 0, null);
        var f2 = new FailedMessage(msg1.Id, msg1.Id, "T", default, null, null, headers, DateTimeOffset.MinValue, DateTimeOffset.MinValue, null, 0, null);
        (f1 == f2).Should().BeTrue();
        (f1 != f2).Should().BeFalse();
        f1.Equals(f2).Should().BeTrue();
        f1.Equals((object)f2).Should().BeTrue();
        f1.GetHashCode().Should().Be(f2.GetHashCode());
        f1.ToString().Should().NotBeNullOrWhiteSpace();
        
        // IdempotencyRecord
        var id1 = new IdempotencyRecord("m1", "c1", DateTimeOffset.MinValue);
        var id2 = new IdempotencyRecord("m1", "c1", DateTimeOffset.MinValue);
        (id1 == id2).Should().BeTrue();
        (id1 != id2).Should().BeFalse();
        id1.Equals(id2).Should().BeTrue();
        id1.Equals((object)id2).Should().BeTrue();
        id1.GetHashCode().Should().Be(id2.GetHashCode());
        id1.ToString().Should().NotBeNullOrWhiteSpace();
        
        // Lease
        var l1 = new Lease("o1", "node1", DateTimeOffset.MinValue);
        var l2 = new Lease("o1", "node1", DateTimeOffset.MinValue);
        (l1 == l2).Should().BeTrue();
        (l1 != l2).Should().BeFalse();
        l1.Equals(l2).Should().BeTrue();
        l1.Equals((object)l2).Should().BeTrue();
        l1.GetHashCode().Should().Be(l2.GetHashCode());
        l1.ToString().Should().NotBeNullOrWhiteSpace();
        l1.IsExpired(DateTimeOffset.MaxValue).Should().BeTrue();
        
        // Lock
        var lk1 = new EricksonLopez.Outbox.Lock("k1", "o1");
        var lk2 = new EricksonLopez.Outbox.Lock("k1", "o1");
        (lk1 == lk2).Should().BeTrue();
        (lk1 != lk2).Should().BeFalse();
        lk1.Equals(lk2).Should().BeTrue();
        lk1.Equals((object)lk2).Should().BeTrue();
        lk1.GetHashCode().Should().Be(lk2.GetHashCode());
        lk1.ToString().Should().NotBeNullOrWhiteSpace();
        
        // Publisher
        var p1 = Publisher.Create("n1");
        var p2 = new Publisher(p1.Id, "n1", p1.RegisteredAt);
        (p1 == p2).Should().BeTrue();
        (p1 != p2).Should().BeFalse();
        p1.Equals(p2).Should().BeTrue();
        p1.Equals((object)p2).Should().BeTrue();
        p1.GetHashCode().Should().Be(p2.GetHashCode());
        p1.ToString().Should().NotBeNullOrWhiteSpace();
        Publisher.None.Id.Should().Be("00000000000000000000000000000000");
        
        // DispatchContext
        var dc1 = new DispatchContext(default, 1);
        var dc2 = new DispatchContext(default, 1);
        dc1.Equals(dc2).Should().BeTrue();
        dc1.Equals((object)dc2).Should().BeTrue();
        dc1.GetHashCode().Should().Be(dc2.GetHashCode());
        dc1.ToString().Should().NotBeNullOrWhiteSpace();
        
        // DispatchResult
        var dr1 = DispatchResult.Ok();
        var dr2 = new DispatchResult(true, false, null, false);
        (dr1 == dr2).Should().BeTrue();
        (dr1 != dr2).Should().BeFalse();
        dr1.Equals(dr2).Should().BeTrue();
        dr1.Equals((object)dr2).Should().BeTrue();
        dr1.GetHashCode().Should().Be(dr2.GetHashCode());
        dr1.ToString().Should().NotBeNullOrWhiteSpace();
        
        DispatchResult.FailAndRetry(new InvalidOperationException()).ShouldRetry.Should().BeTrue();
        DispatchResult.FailFatal(new InvalidOperationException()).ShouldRetry.Should().BeFalse();

        // MessageEnvelope
        var me1 = new MessageEnvelope<string>("test", new MessageMetadata());
        var me2 = new MessageEnvelope<string>("test", new MessageMetadata());
        (me1 == me2).Should().BeTrue();
        (me1 != me2).Should().BeFalse();
        me1.Equals(me2).Should().BeTrue();
        me1.Equals((object)me2).Should().BeTrue();
        me1.GetHashCode().Should().Be(me2.GetHashCode());
        me1.ToString().Should().NotBeNullOrWhiteSpace();
        
        // MetadataEntry
        var mde1 = new MetadataEntry("k", "v");
        var mde2 = new MetadataEntry("k", "v");
        (mde1 == mde2).Should().BeTrue();
        (mde1 != mde2).Should().BeFalse();
        mde1.Equals(mde2).Should().BeTrue();
        mde1.Equals((object)mde2).Should().BeTrue();
        mde1.GetHashCode().Should().Be(mde2.GetHashCode());
        mde1.ToString().Should().NotBeNullOrWhiteSpace();
    }
}



