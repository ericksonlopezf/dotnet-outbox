#pragma warning disable CA2012
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using EricksonLopez.Outbox.Dispatcher;
using EricksonLopez.Outbox.Diagnostics;
using EricksonLopez.Outbox;
using AwesomeAssertions;
using EricksonLopez.Outbox.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using EricksonLopez.Outbox.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace EricksonLopez.Outbox.Tests.Dispatcher
{
    public class OutboxChannelMutationTests
    {
        [Fact]
        public async Task BuildCachedPipeline_WhenTrue_AvoidsGetMiddlewaresOnDispatch()
        {
            var publisher = Substitute.For<IBrokerPublisher>();
            publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<MessageMetadata>(), Arg.Any<DispatchContext>())
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
                Substitute.For<IErrorSanitizer>());

            serviceProvider.ClearReceivedCalls(); // Clear constructor calls

            var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, Array.Empty<byte>(), DateTimeOffset.UtcNow, null, null, 0, 0, null);
            
            await channel.WriteAsync(msg, default);
            
            // Allow processing to finish
            var innerChannel = channel.GetType()
                .GetField("_channel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(channel) as Channel<OutboxMessage>;
            innerChannel?.Writer.Complete();

            var processTask = channel.ProcessMessagesAsync(default);
            await processTask;

            // If BuildCachedPipeline was not called (mutated), it will fall back to resolving from the scope
            serviceProvider.DidNotReceive().GetService(typeof(IEnumerable<IOutboxMiddleware>));
        }

        [Fact]
        public void BuildMetadata_TransformsHeadersCorrectly()
        {
            var publisher = Substitute.For<IBrokerPublisher>();
            var scopeFactory = Substitute.For<IServiceScopeFactory>();
            
            var channel = new OutboxChannel(
                NullLogger<OutboxChannel>.Instance, 
                publisher, 
                Options.Create(new OutboxDispatcherOptions { HasOnlySingletonMiddlewares = false, ChannelCapacity = 10 }), 
                Options.Create(new OutboxRuntimeOptions()),
                new OutboxMetrics(), 
                scopeFactory,
                Substitute.For<IErrorSanitizer>());

            var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, Array.Empty<byte>(), DateTimeOffset.UtcNow, null, null, 0, 0, null);
            var headers = new Dictionary<string, string> { { "key1", "value1" } };
            
            var method = channel.GetType().GetMethod("BuildMetadata", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var meta = (MessageMetadata)method!.Invoke(null, new object[] { msg, headers })!;
            
            meta.Entries.Should().NotBeNull();
            meta.Entries.Length.Should().Be(1);
            meta.Entries.Span[0].Key.Should().Be("key1");
            meta.Entries.Span[0].Value.Should().Be("value1");
        }
        
        [Fact]
        public void BuildMetadata_EmptyHeaders_LeavesEntriesNull()
        {
            var publisher = Substitute.For<IBrokerPublisher>();
            var scopeFactory = Substitute.For<IServiceScopeFactory>();
            
            var channel = new OutboxChannel(
                NullLogger<OutboxChannel>.Instance, 
                publisher, 
                Options.Create(new OutboxDispatcherOptions { HasOnlySingletonMiddlewares = false, ChannelCapacity = 10 }), 
                Options.Create(new OutboxRuntimeOptions()),
                new OutboxMetrics(), 
                scopeFactory,
                Substitute.For<IErrorSanitizer>());

            var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, Array.Empty<byte>(), DateTimeOffset.UtcNow, null, null, 0, 0, null);
            var headers = new Dictionary<string, string>();
            
            var method = channel.GetType().GetMethod("BuildMetadata", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var meta = (MessageMetadata)method!.Invoke(null, new object[] { msg, headers })!;
            
            meta.Entries.IsEmpty.Should().BeTrue();
        }
    }
}
