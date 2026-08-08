#pragma warning disable CA2012
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using AwesomeAssertions;

using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Dispatcher;
using EricksonLopez.Outbox.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

public class OutboxChannelCoverageTests
{
    private static Microsoft.Extensions.DependencyInjection.IServiceScopeFactory FakeScopeFactory(IServiceProvider provider) { var scope = Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScope>(); scope.ServiceProvider.Returns(provider); var factory = Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(); factory.CreateScope().Returns(scope); return factory; }
    private static void CompleteWriter(OutboxChannel channel)
    {
        var inner = typeof(OutboxChannel)
            .GetField("_channel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(channel) as Channel<OutboxMessage>;
        inner?.Writer.Complete();
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Truncate_Large_Payload()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var options = Options.Create(new OutboxDispatcherOptions { ChannelCapacity = 10 });
        var baseOptions = new OutboxRuntimeOptions() { MaxPayloadSizeInBytes = 100 };
        

        var repo = Substitute.For<IOutboxRepository>();
        var services = new ServiceCollection().AddScoped(sp => repo).AddScoped<IDeadLetterRepository>(sp => null!).BuildServiceProvider();
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, options, Options.Create(baseOptions), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), FakeScopeFactory(services), NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>());

        var message = new OutboxMessage(Guid.NewGuid(), "alias", new byte[101], null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);

        await channel.WriteAsync(message, CancellationToken.None);
        CompleteWriter(channel);
        await channel.ProcessMessagesAsync(CancellationToken.None);

        await repo.Received().MarkAsFailedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), Arg.Any<string>(), true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Use_Header_Cache_For_Same_Headers()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var options = Options.Create(new OutboxDispatcherOptions { ChannelCapacity = 10 });
        

        var repo = Substitute.For<IOutboxRepository>();
        var services = new ServiceCollection().AddScoped(sp => repo).AddScoped<IDeadLetterRepository>(sp => null!).BuildServiceProvider();
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, options, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), FakeScopeFactory(services), NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>());

        var headers = System.Text.Encoding.UTF8.GetBytes("{\"traceparent\":\"123\"}");
        var msg1 = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, headers, DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var msg2 = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, headers, DateTimeOffset.UtcNow, null, null, 0, 0, null);

        await channel.WriteAsync(msg1, CancellationToken.None);
        await channel.WriteAsync(msg2, CancellationToken.None);
        CompleteWriter(channel);

        publisher.PublishRawAsync(default!, default!, default!)
            .ReturnsForAnyArgs(new ValueTask<DispatchResult>(DispatchResult.Ok()));

        await channel.ProcessMessagesAsync(CancellationToken.None);

        await repo.Received().MarkAsDispatchedAsync(Arg.Is<IReadOnlyList<OutboxMessage>>(l => l.Count == 2), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Handle_Partial_Batch_Success()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var options = Options.Create(new OutboxDispatcherOptions { ChannelCapacity = 10 });
        

        var repo = Substitute.For<IOutboxRepository>();
        var services = new ServiceCollection().AddScoped(sp => repo).AddScoped<IDeadLetterRepository>(sp => null!).BuildServiceProvider();
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, options, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), FakeScopeFactory(services), NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>());

        var msg1 = new OutboxMessage(Guid.NewGuid(), "alias1", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var msg2 = new OutboxMessage(Guid.NewGuid(), "alias2", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);

        await channel.WriteAsync(msg1, CancellationToken.None);
        await channel.WriteAsync(msg2, CancellationToken.None);
        CompleteWriter(channel);

        publisher.PublishRawAsync(Arg.Is<OutboxMessage>(m => m.Id == msg1.Id), Arg.Any<MessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.Ok()));
            
        publisher.PublishRawAsync(Arg.Is<OutboxMessage>(m => m.Id == msg2.Id), Arg.Any<MessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.FailAndRetry(new InvalidOperationException("test"))));

        await channel.ProcessMessagesAsync(CancellationToken.None);

        await repo.Received().MarkAsDispatchedAsync(Arg.Is<IReadOnlyList<OutboxMessage>>(l => l.Count == 1 && l[0].Id == msg1.Id), Arg.Any<CancellationToken>());
        await repo.Received().MarkAsFailedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), Arg.Any<string>(), false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Cancel_During_Batch_Iteration()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var options = Options.Create(new OutboxDispatcherOptions { ChannelCapacity = 10 });
        

        var repo = Substitute.For<IOutboxRepository>();
        var services = new ServiceCollection().AddScoped(sp => repo).AddScoped<IDeadLetterRepository>(sp => null!).BuildServiceProvider();
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, options, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), FakeScopeFactory(services), NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>());

        var msg1 = new OutboxMessage(Guid.NewGuid(), "alias1", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var msg2 = new OutboxMessage(Guid.NewGuid(), "alias2", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);

        await channel.WriteAsync(msg1, CancellationToken.None);
        await channel.WriteAsync(msg2, CancellationToken.None);
        
        var cts = new CancellationTokenSource();

        publisher.PublishRawAsync(Arg.Is<OutboxMessage>(m => m.Id == msg1.Id), Arg.Any<MessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(x => 
            {
                cts.Cancel(); // Cancel while processing the first message
                return new ValueTask<DispatchResult>(DispatchResult.Ok());
            });

        await channel.ProcessMessagesAsync(cts.Token);

        // msg2 should NOT be processed because loop broke
        await publisher.DidNotReceive().PublishRawAsync(Arg.Is<OutboxMessage>(m => m.Id == msg2.Id), Arg.Any<MessageMetadata>(), Arg.Any<DispatchContext>());
        
        // MarkAsDispatchedAsync should have been called for msg1 since it succeeded before the break
        await repo.Received().MarkAsDispatchedAsync(Arg.Is<IReadOnlyList<OutboxMessage>>(l => l.Count == 1 && l[0].Id == msg1.Id), Arg.Any<CancellationToken>());
    }
}



