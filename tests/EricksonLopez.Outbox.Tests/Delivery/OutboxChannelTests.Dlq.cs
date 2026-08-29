// Copyright © Erickson Lopez. MIT License.
#pragma warning disable CA2012, CS8600, CS8602
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
using EricksonLopez.Outbox.Tests.Infrastructure;
using EricksonLopez.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Delivery;

public partial class OutboxChannelTests
{
    public class DlqTests
    {
        private readonly OutboxChannel _channel;
        private readonly IBrokerPublisher _publisher = Substitute.For<IBrokerPublisher>();
        private readonly IOutboxRepository _repository = Substitute.For<IOutboxRepository>();
        private readonly IDeadLetterRepository _dlqRepository = Substitute.For<IDeadLetterRepository>();
        private readonly IServiceScopeFactory _scopeFactory = Substitute.For<IServiceScopeFactory>();
        private readonly IServiceScope _scope = Substitute.For<IServiceScope>();
        private readonly IServiceProvider _serviceProvider = Substitute.For<IServiceProvider>();

        public DlqTests()
        {
            _scopeFactory.CreateScope().Returns(_scope);
            _scope.ServiceProvider.Returns(_serviceProvider);
            _serviceProvider.GetService(typeof(IOutboxRepository)).Returns(_repository);
            _serviceProvider.GetService(typeof(IDeadLetterRepository)).Returns(_dlqRepository);
            _serviceProvider.GetService(typeof(IEnumerable<IOutboxMiddleware>)).Returns(Array.Empty<IOutboxMiddleware>());

            var options = Options.Create(new OutboxDispatcherOptions { ChannelCapacity = 10 });
            var runtimeOptions = Options.Create(new OutboxRuntimeOptions());

            _channel = new OutboxChannel(
                NullLogger<OutboxChannel>.Instance,
                _publisher,
                options,
                runtimeOptions,
                new OutboxMetrics(Substitute.For<System.Diagnostics.Metrics.IMeterFactory>()),
                _scopeFactory,
                new DefaultErrorSanitizer(),
                TimeProvider.System
            );
        }

        [Fact]
        public async Task ProcessMessagesAsync_Failed_NoRetry_MarksAsFailed()
        {
            var msg = new OutboxMessageTestDataBuilder()
                .WithMessageType("Type")
                .WithPayload(ReadOnlyMemory<byte>.Empty)
                .WithHeaders(ReadOnlyMemory<byte>.Empty)
                .Build();
            
            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.FailFatal(new InvalidOperationException("Fatal error")));

            await _channel.WriteAsync(msg, CancellationToken.None);
            _channel.Complete();

            await _channel.ProcessMessagesAsync(CancellationToken.None);

            await _repository.Received(1).MarkAsFailedAsync(
                Arg.Is<IReadOnlyList<OutboxMessage>>(l => l.Any(m => m.Id == msg.Id)),
                "Fatal error",
                true,
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ProcessMessagesAsync_Failed_MaxRetriesReached_MarksAsDeadLetter()
        {
            var msg = new OutboxMessageTestDataBuilder()
                .WithMessageType("Type")
                .WithPayload(ReadOnlyMemory<byte>.Empty)
                .WithHeaders(ReadOnlyMemory<byte>.Empty)
                .WithRetryCount(9)
                .Build();
            
            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.FailAndRetry(new InvalidOperationException("Transient error")));

            await _channel.WriteAsync(msg, CancellationToken.None);
            _channel.Complete();

            await _channel.ProcessMessagesAsync(CancellationToken.None);

            await _repository.Received(1).MarkAsFailedAsync(
                Arg.Is<IReadOnlyList<OutboxMessage>>(l => l.Any(m => m.Id == msg.Id)),
                "Transient error",
                true,
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ProcessMessagesAsync_Failed_NotMaxRetries_ShouldRetry_MarksAsFailedNotDeadLetter()
        {
            var msg = new OutboxMessageTestDataBuilder()
                .WithMessageType("Type")
                .WithPayload(ReadOnlyMemory<byte>.Empty)
                .WithHeaders(ReadOnlyMemory<byte>.Empty)
                .WithRetryCount(1)
                .Build();
            
            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.FailAndRetry(new InvalidOperationException("Transient error")));

            await _channel.WriteAsync(msg, CancellationToken.None);
            _channel.Complete();

            await _channel.ProcessMessagesAsync(CancellationToken.None);

            await _repository.Received(1).MarkAsFailedAsync(
                Arg.Is<IReadOnlyList<OutboxMessage>>(l => l.Any(m => m.Id == msg.Id)),
                "Transient error",
                false,
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ProcessMessagesAsync_DlqReason_FatalFailure_When_NotShouldRetry()
        {
            var msg = new OutboxMessage(Guid.NewGuid(), "OrderCreated", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.FailFatal(new InvalidOperationException("Fatal error")));

            await _channel.WriteAsync(msg, CancellationToken.None);
            _channel.Complete();

            await _channel.ProcessMessagesAsync(CancellationToken.None);

            await _dlqRepository.Received(1).InsertAsync(Arg.Is<DeadLetterMessage>(dlq => dlq.Reason == "Fatal failure"), default, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ProcessMessagesAsync_DlqReason_MaxRetriesReached_When_ShouldRetry_And_MaxRetries()
        {
            var msg = new OutboxMessage(Guid.NewGuid(), "OrderCreated", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 9, null);
            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.FailAndRetry(new InvalidOperationException("Transient error")));

            await _channel.WriteAsync(msg, CancellationToken.None);
            _channel.Complete();

            await _channel.ProcessMessagesAsync(CancellationToken.None);

            await _dlqRepository.Received(1).InsertAsync(Arg.Is<DeadLetterMessage>(dlq => dlq.Reason == "Max retries reached"), default, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ProcessMessagesAsync_WhenDlqInsertThrows_StillMarksAsDeadLetterInRepo()
        {
            var msg = new OutboxMessage(Guid.NewGuid(), "OrderCreated", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 9, null);
            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.FailAndRetry(new InvalidOperationException("Transient error")));

            _dlqRepository.InsertAsync(Arg.Any<DeadLetterMessage>(), default, Arg.Any<CancellationToken>())
                .Returns(_ => throw new InvalidOperationException("DLQ DB error"));

            await _channel.WriteAsync(msg, CancellationToken.None);
            _channel.Complete();

            await _channel.ProcessMessagesAsync(CancellationToken.None);

            await _repository.Received(1).MarkAsFailedAsync(
                Arg.Is<IReadOnlyList<OutboxMessage>>(l => l.Any(m => m.Id == msg.Id)),
                Arg.Any<string>(),
                true,
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ProcessMessagesAsync_WhenIncrementRetryCountIsFalse_DoesNotMarkFailed()
        {
            var msg = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 1, null);
            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.FailAndRetry(new InvalidOperationException("Delay"), incrementRetryCount: false));

            await _channel.WriteAsync(msg, CancellationToken.None);
            _channel.Complete();

            await _channel.ProcessMessagesAsync(CancellationToken.None);

            await _repository.DidNotReceive().MarkAsFailedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ProcessMessagesAsync_MixedBatch_FlushesDispatchedOnly()
        {
            var msg1 = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
            var msg2 = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);

            _publisher.PublishRawAsync(Arg.Is<OutboxMessage>(m => m.Id == msg1.Id), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.Ok());
            _publisher.PublishRawAsync(Arg.Is<OutboxMessage>(m => m.Id == msg2.Id), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.FailFatal(new InvalidOperationException("Fail msg 2")));

            await _channel.WriteAsync(msg1, CancellationToken.None);
            await _channel.WriteAsync(msg2, CancellationToken.None);
            _channel.Complete();

            await _channel.ProcessMessagesAsync(CancellationToken.None);

            await _repository.Received(1).MarkAsDispatchedAsync(Arg.Is<IReadOnlyList<OutboxMessage>>(list => list.Count == 1 && list[0].Id == msg1.Id), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ProcessMessagesAsync_ConsecutiveBatches_ClearsBatchAndDispatchedIds()
        {
            var msg1 = new OutboxMessage(Guid.NewGuid(), "Type1", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
            var msg2 = new OutboxMessage(Guid.NewGuid(), "Type2", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);

            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.Ok());

            var batches = new List<IReadOnlyList<OutboxMessage>>();
            _repository.MarkAsDispatchedAsync(Arg.Do<IReadOnlyList<OutboxMessage>>(list => batches.Add(new List<OutboxMessage>(list))), Arg.Any<CancellationToken>())
                .Returns(ValueTask.CompletedTask);

            await _channel.WriteAsync(msg1, CancellationToken.None);
            await _channel.WriteAsync(msg2, CancellationToken.None);
            _channel.Complete();

            await _channel.ProcessMessagesAsync(CancellationToken.None);

            batches.Count.Should().BeGreaterThanOrEqualTo(1);
        }

        [Fact]
        public async Task ProcessMessagesAsync_MoreThan100Messages_BatchCapsAt100()
        {
            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.Ok());

            var batches = new List<IReadOnlyList<OutboxMessage>>();
            _repository.MarkAsDispatchedAsync(Arg.Do<IReadOnlyList<OutboxMessage>>(list => batches.Add(new List<OutboxMessage>(list))), Arg.Any<CancellationToken>())
                .Returns(ValueTask.CompletedTask);

            var options = Options.Create(new OutboxDispatcherOptions { ChannelCapacity = 200 });
            var channel = new OutboxChannel(
                NullLogger<OutboxChannel>.Instance,
                _publisher,
                options,
                Options.Create(new OutboxRuntimeOptions()),
                new OutboxMetrics(Substitute.For<System.Diagnostics.Metrics.IMeterFactory>()),
                _scopeFactory,
                new DefaultErrorSanitizer(),
                TimeProvider.System
            );

            for (int i = 0; i < 105; i++)
            {
                var msg = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
                await channel.WriteAsync(msg, CancellationToken.None);
            }
            channel.Complete();

            await channel.ProcessMessagesAsync(CancellationToken.None);

            batches[0].Count.Should().Be(100);
            batches[1].Count.Should().Be(5);
        }

        [Fact]
        public async Task ProcessMessagesAsync_FatalFailure_InsertsToDlqWithFatalReason()
        {
            var msg = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.FailFatal(new InvalidOperationException("Fatal error")));

            await _channel.WriteAsync(msg, CancellationToken.None);
            _channel.Complete();

            await _channel.ProcessMessagesAsync(CancellationToken.None);

            await _dlqRepository.Received(1).InsertAsync(Arg.Is<DeadLetterMessage>(d => d.OriginalMessageId == msg.Id && d.Reason == "Fatal failure"), Arg.Is<IOutboxTransactionContext?>(_ => true), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ProcessMessagesAsync_MaxRetriesReached_InsertsToDlqWithMaxRetriesReason()
        {
            var msg = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 10, null);
            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.FailAndRetry(new InvalidOperationException("Retry error")));

            await _channel.WriteAsync(msg, CancellationToken.None);
            _channel.Complete();

            await _channel.ProcessMessagesAsync(CancellationToken.None);

            await _dlqRepository.Received(1).InsertAsync(Arg.Is<DeadLetterMessage>(d => d.OriginalMessageId == msg.Id && d.Reason == "Max retries reached"), Arg.Is<IOutboxTransactionContext?>(_ => true), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ProcessMessagesAsync_WithSingletonMiddlewares_UsesCachedPipeline()
        {
            var middleware = Substitute.For<IOutboxMiddleware>();
            middleware.InvokeAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<OutboxPipelineDelegate>(), Arg.Any<CancellationToken>())
                .Returns(call => {
                    var m = call.Arg<OutboxMessage>();
                    var meta = call.Arg<OutboxMessageMetadata>();
                    var next = call.Arg<OutboxPipelineDelegate>();
                    var ct = call.Arg<CancellationToken>();
                    return next(m, meta, ct);
                });

            _serviceProvider.GetService(typeof(IEnumerable<IOutboxMiddleware>)).Returns(new[] { middleware });

            var options = Options.Create(new OutboxDispatcherOptions { HasOnlySingletonMiddlewares = true });
            var channel = new OutboxChannel(
                NullLogger<OutboxChannel>.Instance,
                _publisher,
                options,
                Options.Create(new OutboxRuntimeOptions()),
                new OutboxMetrics(Substitute.For<System.Diagnostics.Metrics.IMeterFactory>()),
                _scopeFactory,
                new DefaultErrorSanitizer(), TimeProvider.System
            );

            var msg = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.Ok());

            await channel.WriteAsync(msg, CancellationToken.None);
            channel.Complete();

            await channel.ProcessMessagesAsync(CancellationToken.None);

            await middleware.Received(1).InvokeAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<OutboxPipelineDelegate>(), Arg.Any<CancellationToken>());
            await _repository.Received(1).MarkAsDispatchedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), Arg.Any<CancellationToken>());

            // White-box test rationale: asserts pipeline singleton instance caching without allocating a delegate per message
            var cachedPipeline = ReflectionTestHelper.GetFieldValue<OutboxPipeline?>(channel, "_cachedPipeline");
            cachedPipeline.Should().NotBeNull();
        }

        [Fact]
        public async Task ProcessMessagesAsync_WithScopedMiddlewares_ExecutesPipeline()
        {
            var middleware = Substitute.For<IOutboxMiddleware>();
            middleware.InvokeAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<OutboxPipelineDelegate>(), Arg.Any<CancellationToken>())
                .Returns(call => {
                    var m = call.Arg<OutboxMessage>();
                    var meta = call.Arg<OutboxMessageMetadata>();
                    var next = call.Arg<OutboxPipelineDelegate>();
                    var ct = call.Arg<CancellationToken>();
                    return next(m, meta, ct);
                });

            _serviceProvider.GetService(typeof(IEnumerable<IOutboxMiddleware>)).Returns(new[] { middleware });

            var options = Options.Create(new OutboxDispatcherOptions { HasOnlySingletonMiddlewares = false });
            var channel = new OutboxChannel(
                NullLogger<OutboxChannel>.Instance,
                _publisher,
                options,
                Options.Create(new OutboxRuntimeOptions()),
                new OutboxMetrics(Substitute.For<System.Diagnostics.Metrics.IMeterFactory>()),
                _scopeFactory,
                new DefaultErrorSanitizer(), TimeProvider.System
            );

            var msg = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.Ok());

            await channel.WriteAsync(msg, CancellationToken.None);
            channel.Complete();

            await channel.ProcessMessagesAsync(CancellationToken.None);

            await middleware.Received(1).InvokeAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<OutboxPipelineDelegate>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ProcessMessagesAsync_TransientFailure_CallsMarkAsFailedAsync()
        {
            var msg = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.FailAndRetry(new InvalidOperationException("Transient")));

            await _channel.WriteAsync(msg, CancellationToken.None);
            _channel.Complete();

            await _channel.ProcessMessagesAsync(CancellationToken.None);

            await _repository.Received(1).MarkAsFailedAsync(
                Arg.Is<IReadOnlyList<OutboxMessage>>(list => list.Count == 1 && list[0].Id == msg.Id),
                Arg.Any<string>(),
                Arg.Is<bool>(b => !b),
                Arg.Any<CancellationToken>());

            await _dlqRepository.DidNotReceiveWithAnyArgs().InsertAsync(default!);
        }

        [Fact]
        public async Task ProcessMessagesAsync_MultipleBatches_ClearsDispatchedIdsBetweenBatches()
        {
            var msg1 = new OutboxMessage(Guid.NewGuid(), "Type1", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
            var msg2 = new OutboxMessage(Guid.NewGuid(), "Type2", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);

            var batch1DispatchedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var batch2FailedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            _repository.MarkAsDispatchedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), Arg.Any<CancellationToken>())
                .Returns(_ => {
                    batch1DispatchedTcs.TrySetResult();
                    return ValueTask.CompletedTask;
                });

            _repository.MarkAsFailedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns(_ => {
                    batch2FailedTcs.TrySetResult();
                    return ValueTask.CompletedTask;
                });

            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(call => {
                    var m = call.Arg<OutboxMessage>();
                    if (m.Id == msg1.Id) return ValueTask.FromResult(DispatchResult.Ok());
                    return ValueTask.FromResult(DispatchResult.FailAndRetry(new InvalidOperationException("Fail")));
                });

            // Write batch 1
            await _channel.WriteAsync(msg1, CancellationToken.None);

            using var cts = new CancellationTokenSource();
            var processTask = Task.Run(async () => await _channel.ProcessMessagesAsync(cts.Token));

            // Wait deterministically for batch 1 to be marked as dispatched
            await batch1DispatchedTcs.Task.WaitAsync(TimeSpan.FromSeconds(15));

            // Write batch 2
            await _channel.WriteAsync(msg2, CancellationToken.None);
            await batch2FailedTcs.Task.WaitAsync(TimeSpan.FromSeconds(15));

            _channel.Complete();
            cts.Cancel();
            try { await processTask; } catch (OperationCanceledException) { }

            // Batch 1 had 1 message dispatched -> MarkAsDispatchedAsync called once
            // Batch 2 had 0 messages dispatched -> MarkAsDispatchedAsync MUST NOT be called again
            await _repository.Received(1).MarkAsDispatchedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ExecuteDbWithRetryAsync_WhenRetriesExhausted_ThrowsException()
        {
            // White-box test rationale: tests private DB retry loop resilience and max attempt threshold in isolation
            var method = ReflectionTestHelper.GetMethodOrThrow(typeof(OutboxChannel), "ExecuteDbWithRetryAsync");

            int attempts = 0;
            Func<CancellationToken, ValueTask> failingOp = ct => {
                attempts++;
                throw new InvalidOperationException("DB failed");
            };

            var options = Options.Create(new OutboxDispatcherOptions { DbRetryMaxAttempts = 2, DbRetryBaseDelayMs = 1 });
            var channel = new OutboxChannel(
                NullLogger<OutboxChannel>.Instance,
                _publisher,
                options,
                Options.Create(new OutboxRuntimeOptions()),
                new OutboxMetrics(Substitute.For<System.Diagnostics.Metrics.IMeterFactory>()),
                _scopeFactory,
                new DefaultErrorSanitizer(), TimeProvider.System
            );

            Func<Task> act = async () => {
                try
                {
                    var vt = (ValueTask)method.Invoke(channel, new object[] { failingOp, CancellationToken.None })!;
                    await vt;
                }
                catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException != null)
                {
                    throw ex.InnerException;
                }
            };

            await act.Should().ThrowAsync<InvalidOperationException>();
            attempts.Should().Be(3); // attempt 0, 1, 2 = 3 total calls
        }

        [Fact]
        public void BuildCachedPipeline_WhenMiddlewaresNull_CachedPipelineIsNull()
        {
            var sp = Substitute.For<IServiceProvider>();
            var scopeFactory = Substitute.For<IServiceScopeFactory>();
            var scope = Substitute.For<IServiceScope>();
            scope.ServiceProvider.Returns(sp);
            scopeFactory.CreateScope().Returns(scope);

            var options = Options.Create(new OutboxDispatcherOptions { HasOnlySingletonMiddlewares = false });
            var channel = new OutboxChannel(
                NullLogger<OutboxChannel>.Instance,
                _publisher,
                options,
                Options.Create(new OutboxRuntimeOptions()),
                new OutboxMetrics(Substitute.For<System.Diagnostics.Metrics.IMeterFactory>()),
                scopeFactory,
                new DefaultErrorSanitizer(), TimeProvider.System
            );

            // White-box test rationale: verifies no cached pipeline instance is built when non-singleton middlewares are present
            var cachedPipeline = ReflectionTestHelper.GetFieldValue<OutboxPipeline?>(channel, "_cachedPipeline");
            cachedPipeline.Should().BeNull();
        }

        [Fact]
        public async Task FillBatchFast_WhenElapsedGreaterOrEqualTo50ms_BreaksEarly()
        {
            var msg = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
            await _channel.WriteAsync(msg, CancellationToken.None);

            // White-box test rationale: tests internal fast batch-filling logic boundary under simulated elapsed tick threshold
            var method = ReflectionTestHelper.GetMethodOrThrow(typeof(OutboxChannel), "FillBatchFast");
            var batch = new List<OutboxMessage>();
            method.Invoke(_channel, new object[] { batch, Environment.TickCount64 - 100 });

            batch.Should().HaveCount(1);
        }

        [Theory]
        [InlineData(100, 100, true)]   // message payload == limit -> should fallback log
        [InlineData(101, 100, false)]  // message payload > limit -> skip fallback log
        public async Task ProcessMessagesAsync_DlqPayloadFallback_WhenPayloadWithinOrExceedingLimit(int payloadSize, int maxPayloadSize, bool expectFallbackLog)
        {
            var logger = new FakeChannelLogger();
            var runtimeOptions = new OutboxRuntimeOptions
            {
                MaxPayloadSizeInBytes = maxPayloadSize
            };
            var channel = CreateTestChannel(_publisher, _repository, _dlqRepository, logger: logger, runtimeOptions: runtimeOptions);

            var payload = new byte[payloadSize];
            var msg = new OutboxMessage(Guid.NewGuid(), "Type", payload, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);

            // Publisher returns fatal failure
            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.FailFatal(new InvalidOperationException("Fatal broker error")));

            // DLQ repository throws on insert to force fallback log
            _dlqRepository.InsertAsync(Arg.Any<DeadLetterMessage>(), Arg.Any<IOutboxTransactionContext?>(), Arg.Any<CancellationToken>())
                .Returns(ValueTask.FromException(new InvalidOperationException("DLQ insert failed")));

            await channel.WriteAsync(msg, CancellationToken.None);
            channel.Complete();

            await channel.ProcessMessagesAsync(CancellationToken.None);

            var hasDlqFallback = logger.LoggedEvents.Any(e => e.Id == 10012);
            hasDlqFallback.Should().Be(expectFallbackLog);
        }
    }
}

