// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.MassTransit;
using EricksonLopez.Outbox.Persistence;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

#pragma warning disable CA2012
namespace EricksonLopez.Outbox.Tests.MassTransit;

public class InboxIdempotencyFilterTests
{
    public class TestMessage { }

    [Fact]
    public async Task Send_Should_Bypass_If_No_MessageId()
    {
        var context = Substitute.For<ConsumeContext<TestMessage>>();
        context.MessageId.Returns((Guid?)null);
        var next = Substitute.For<IPipe<ConsumeContext<TestMessage>>>();

        var filter = new InboxIdempotencyFilter<TestMessage>();
        await filter.Send(context, next);

        await next.Received(1).Send(context);
    }

    [Fact]
    public async Task Send_Should_Bypass_If_No_ServiceProvider()
    {
        var context = Substitute.For<ConsumeContext<TestMessage>>();
        context.MessageId.Returns(Guid.NewGuid());
        IServiceProvider? sp = null;
        context.TryGetPayload(out sp).Returns(false);
        
        var next = Substitute.For<IPipe<ConsumeContext<TestMessage>>>();

        var filter = new InboxIdempotencyFilter<TestMessage>();
        await filter.Send(context, next);

        await next.Received(1).Send(context);
    }

    [Fact]
    public async Task Send_Should_Bypass_If_ServiceProvider_Is_Null()
    {
        var context = Substitute.For<ConsumeContext<TestMessage>>();
        context.MessageId.Returns(Guid.NewGuid());
        context.TryGetPayload(out Arg.Any<IServiceProvider?>()).Returns(x => 
        {
            x[0] = null;
            return true;
        });
        
        var next = Substitute.For<IPipe<ConsumeContext<TestMessage>>>();

        var filter = new InboxIdempotencyFilter<TestMessage>();
        await filter.Send(context, next);

        await next.Received(1).Send(context);
    }

    [Fact]
    public async Task Send_Should_Bypass_If_No_Dependencies()
    {
        var context = Substitute.For<ConsumeContext<TestMessage>>();
        context.MessageId.Returns(Guid.NewGuid());
        
        var sp = Substitute.For<IServiceProvider>();
        context.TryGetPayload(out Arg.Any<IServiceProvider?>()).Returns(x => 
        {
            x[0] = sp;
            return true;
        });

        var next = Substitute.For<IPipe<ConsumeContext<TestMessage>>>();

        var filter = new InboxIdempotencyFilter<TestMessage>();
        await filter.Send(context, next);

        await next.Received(1).Send(context);
    }
    
    [Fact]
    public async Task Send_Should_Bypass_If_Missing_IdempotencyRepository()
    {
        var context = Substitute.For<ConsumeContext<TestMessage>>();
        context.MessageId.Returns(Guid.NewGuid());
        
        var sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(EricksonLopez.Outbox.Persistence.IOutboxTransactionContext))
            .Returns(Substitute.For<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>());
        
        context.TryGetPayload(out Arg.Any<IServiceProvider?>()).Returns(x => 
        {
            x[0] = sp;
            return true;
        });

        var next = Substitute.For<IPipe<ConsumeContext<TestMessage>>>();

        var filter = new InboxIdempotencyFilter<TestMessage>();
        await filter.Send(context, next);

        await next.Received(1).Send(context);
    }
    
    [Fact]
    public async Task Send_Should_Bypass_If_Missing_TransactionContext()
    {
        var context = Substitute.For<ConsumeContext<TestMessage>>();
        context.MessageId.Returns(Guid.NewGuid());
        
        var sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(IIdempotencyRepository))
            .Returns(Substitute.For<IIdempotencyRepository>());
        
        context.TryGetPayload(out Arg.Any<IServiceProvider?>()).Returns(x => 
        {
            x[0] = sp;
            return true;
        });

        var next = Substitute.For<IPipe<ConsumeContext<TestMessage>>>();

        var filter = new InboxIdempotencyFilter<TestMessage>();
        await filter.Send(context, next);

        await next.Received(1).Send(context);
    }

    [Fact]
    public async Task Send_Should_Call_Next_If_New_Message()
    {
        var context = Substitute.For<ConsumeContext<TestMessage>>();
        context.MessageId.Returns(Guid.NewGuid());
        var rc = Substitute.For<ReceiveContext>();
        rc.InputAddress.Returns(new Uri("queue:test"));
        context.ReceiveContext.Returns(rc);
        
        var sp = Substitute.For<IServiceProvider>();
        var repo = Substitute.For<IIdempotencyRepository>();
        var tx = Substitute.For<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>();
        _ = repo.TryInsertAsync(Arg.Is<IdempotencyRecord>(r => 
            r.MessageId == context.MessageId!.Value.ToString() &&
            r.ConsumerId == "queue:test"
        ), tx, Arg.Any<CancellationToken>()).Returns(new ValueTask<bool>(true));
        
        sp.GetService(typeof(IIdempotencyRepository)).Returns(repo);
        sp.GetService(typeof(EricksonLopez.Outbox.Persistence.IOutboxTransactionContext)).Returns(tx);

        context.TryGetPayload(out Arg.Any<IServiceProvider?>()).Returns(x => 
        {
            x[0] = sp;
            return true;
        });

        var next = Substitute.For<IPipe<ConsumeContext<TestMessage>>>();

        var filter = new InboxIdempotencyFilter<TestMessage>();
        await filter.Send(context, next);

        await next.Received(1).Send(context);
    }

    [Fact]
    public async Task Send_Should_ShortCircuit_If_Duplicate_Message()
    {
        var context = Substitute.For<ConsumeContext<TestMessage>>();
        context.MessageId.Returns(Guid.NewGuid());
        var rc = Substitute.For<ReceiveContext>();
        rc.InputAddress.Returns(new Uri("queue:test"));
        context.ReceiveContext.Returns(rc);
        
        var sp = Substitute.For<IServiceProvider>();
        var repo = Substitute.For<IIdempotencyRepository>();
        var tx = Substitute.For<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>();
        _ = repo.TryInsertAsync(Arg.Is<IdempotencyRecord>(r => 
            r.MessageId == context.MessageId!.Value.ToString() &&
            r.ConsumerId == "queue:test"
        ), tx, Arg.Any<CancellationToken>()).Returns(new ValueTask<bool>(false));
        
        sp.GetService(typeof(IIdempotencyRepository)).Returns(repo);
        sp.GetService(typeof(EricksonLopez.Outbox.Persistence.IOutboxTransactionContext)).Returns(tx);

        context.TryGetPayload(out Arg.Any<IServiceProvider?>()).Returns(x => 
        {
            x[0] = sp;
            return true;
        });

        var next = Substitute.For<IPipe<ConsumeContext<TestMessage>>>();

        var filter = new InboxIdempotencyFilter<TestMessage>();
        await filter.Send(context, next);

        await next.DidNotReceive().Send(context);
    }

    [Fact]
    public async Task Send_WhenInputAddressIsNull_UsesUnknownQueueConsumerId()
    {
        var context = Substitute.For<ConsumeContext<TestMessage>>();
        context.MessageId.Returns(Guid.NewGuid());
        var rc = Substitute.For<ReceiveContext>();
        rc.InputAddress.Returns((Uri?)null);
        context.ReceiveContext.Returns(rc);

        var sp = Substitute.For<IServiceProvider>();
        var repo = Substitute.For<IIdempotencyRepository>();
        var tx = Substitute.For<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>();
        _ = repo.TryInsertAsync(Arg.Is<IdempotencyRecord>(r =>
            r.MessageId == context.MessageId!.Value.ToString() &&
            r.ConsumerId == "UnknownQueue"
        ), tx, Arg.Any<CancellationToken>()).Returns(new ValueTask<bool>(true));

        sp.GetService(typeof(IIdempotencyRepository)).Returns(repo);
        sp.GetService(typeof(EricksonLopez.Outbox.Persistence.IOutboxTransactionContext)).Returns(tx);

        context.TryGetPayload(out Arg.Any<IServiceProvider?>()).Returns(x =>
        {
            x[0] = sp;
            return true;
        });

        var next = Substitute.For<IPipe<ConsumeContext<TestMessage>>>();
        var filter = new InboxIdempotencyFilter<TestMessage>();
        await filter.Send(context, next);

        await next.Received(1).Send(context);
    }

    [Fact]
    public void Probe_Should_Configure_Scope()
    {
        var context = Substitute.For<ProbeContext>();
        var scope = Substitute.For<ProbeContext>();
        context.CreateScope("filters").Returns(scope);

        var filter = new InboxIdempotencyFilter<TestMessage>();
        filter.Probe(context);

        context.Received(1).CreateScope("filters");
        scope.Received(1).Add("filterType", "ericksonlopez-outbox-idempotency");
    }
}





