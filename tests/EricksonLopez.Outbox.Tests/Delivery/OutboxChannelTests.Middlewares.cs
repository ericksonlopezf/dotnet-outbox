// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Diagnostics;
using EricksonLopez.Outbox.Dispatcher;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

#pragma warning disable CA2012, CS8600, CS8602
namespace EricksonLopez.Outbox.Tests.Delivery;

public partial class OutboxChannelTests
{
    [Collection("ActivitySource")]
    public class MiddlewaresTests
    {
        [Fact]
        public void Constructor_With_HasOnlySingletonMiddlewares_True_Should_PreResolve_Middlewares()
        {
            var publisher = Substitute.For<IBrokerPublisher>();
            var options = Options.Create(new OutboxDispatcherOptions { ChannelCapacity = 10, HasOnlySingletonMiddlewares = true });
            
            var mw = Substitute.For<IOutboxMiddleware>();
            var services = new ServiceCollection().AddSingleton(mw).BuildServiceProvider();
            var scopeFactory = Substitute.For<IServiceScopeFactory>();
            var scope = Substitute.For<IServiceScope>();
            scope.ServiceProvider.Returns(services);
            scopeFactory.CreateScope().Returns(scope);

            var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, options, Options.Create(new OutboxRuntimeOptions()), new OutboxMetrics(), scopeFactory, FakeErrorSanitizer(), TimeProvider.System);
            
            scopeFactory.Received(1).CreateScope();
            channel.Should().NotBeNull();
        }

        [Fact]
        public async Task BuildCachedPipeline_WhenTrue_AvoidsGetMiddlewaresOnDispatch()
        {
            var publisher = Substitute.For<IBrokerPublisher>();
            publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(ValueTask.FromResult(DispatchResult.Ok()));

            var serviceProvider = Substitute.For<IServiceProvider>();
            serviceProvider.GetService(typeof(IEnumerable<IOutboxMiddleware>)).Returns(Array.Empty<IOutboxMiddleware>());
            serviceProvider.GetService(typeof(IOutboxRepository)).Returns(Substitute.For<IOutboxRepository>());
            serviceProvider.GetService(typeof(IDeadLetterRepository)).Returns(Substitute.For<IDeadLetterRepository>());

            var scope = Substitute.For<IServiceScope>();
            scope.ServiceProvider.Returns(serviceProvider);

            var scopeFactory = Substitute.For<IServiceScopeFactory>();
            scopeFactory.CreateScope().Returns(scope);

            var options = Options.Create(new OutboxDispatcherOptions { HasOnlySingletonMiddlewares = true, ChannelCapacity = 10 });
            var baseOptions = Options.Create(new OutboxRuntimeOptions());
            
            var channel = new OutboxChannel(
                NullLogger<OutboxChannel>.Instance, 
                publisher, 
                options, 
                baseOptions,
                new OutboxMetrics(), 
                scopeFactory,
                Substitute.For<IErrorSanitizer>(), TimeProvider.System);

            serviceProvider.ClearReceivedCalls();

            var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, Array.Empty<byte>(), DateTimeOffset.UtcNow, null, null, 0, 0, null);
            
            await channel.WriteAsync(msg, default);
            channel.Complete();

            var processTask = channel.ProcessMessagesAsync(default);
            await processTask;

            serviceProvider.DidNotReceive().GetService(typeof(IEnumerable<IOutboxMiddleware>));
        }

        [Fact]
        public void BuildMetadata_TransformsHeadersCorrectly()
        {
            var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, Array.Empty<byte>(), DateTimeOffset.UtcNow, null, null, 0, 0, null);
            var headers = new Dictionary<string, string> { { "key1", "value1" } };
            
            var meta = OutboxChannel.BuildMetadata(msg, headers);
            
            meta.Entries.Should().NotBeNull();
            meta.Entries.Length.Should().Be(1);
            meta.Entries.Span[0].Key.Should().Be("key1");
            meta.Entries.Span[0].Value.Should().Be("value1");
        }
        
        [Fact]
        public void BuildMetadata_EmptyHeaders_LeavesEntriesNull()
        {
            var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, Array.Empty<byte>(), DateTimeOffset.UtcNow, null, null, 0, 0, null);
            var headers = new Dictionary<string, string>();
            
            var meta = OutboxChannel.BuildMetadata(msg, headers);
            
            meta.Entries.IsEmpty.Should().BeTrue();
        }
    }
}

