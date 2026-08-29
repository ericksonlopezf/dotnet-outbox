<!-- Copyright © Erickson Lopez. MIT License. -->

# ADR-006: Asynchronous Dispatch with Bounded Channels

## 1. Title and Status
**Background Event Dispatcher using `System.Threading.Channels`**
*Status:* Approved and Implemented in `EricksonLopez.Outbox` (Core / Dispatcher).

## 2. Context and Motivation
Extracting events from the database is only 50% of the equation. The other 50% is sending them to the Broker (RabbitMQ) massively, rapidly, and without saturating memory or TCP connections.
If the Dispatcher extracts 1,000,000 records and creates 1,000,000 parallel `Task.Run()` calls, we will exhaust the ThreadPool (Thread Starvation) and the application will collapse.
The motivation is to establish exact concurrency control (Backpressure) protecting both the internal ThreadPool and the stability of the Broker.

## 3. Evaluated Alternatives
1. **Parallel.ForEach / Task.WhenAll:** Creates uncontrollable bursts of concurrent threads or pauses the extracting thread until the entire batch finishes (introducing inefficient micro-latencies).
2. **TPL Dataflow (`ActionBlock<T>`):** Powerful, but carries a high heap allocation overhead for simple Producer/Consumer tasks.
3. **`System.Threading.Channels` (`BoundedChannel`):** Native .NET structure optimized for ultra-low-level asynchronous Producer/Consumer queues with native backpressure support.

## 4. Advantages
* **Native Backpressure:** If the Broker (RabbitMQ) slows down, the channel fills up to its limit (e.g., 10,000). Once full, the `BoundedChannelFullMode.Wait` behavior forces the SQL Extractor Worker into a passive pause (without consuming CPU) until the Broker recovers. Absolute prevention against OutOfMemoryExceptions (OOM)!
* **Multiple Consumers:** We can instantiate 1 concurrent DB reader (Producer) and 5 RabbitMQ senders (Consumers), maximizing multiplexed TCP throughput without conflicts.
* **Minimal Allocation:** Modern `Channels` in .NET are hyper-optimized to reduce garbage collections.

## 5. Disadvantages
* **Graceful Shutdown Difficulty:** Coordinating application shutdown requires precise handling of `CancellationToken` and `Channel.Writer.Complete()` to ensure that extracted messages still in the channel are not lost when the host shuts down.

## 6. Trade-offs
We decided to isolate this complexity within an internal `OutboxChannelDispatcher` abstraction injected as an `IHostedService`. We accept the architectural challenge of "Graceful Shutdown" to gain the incomparable benefits of Backpressure.

## 7. Performance Impact
* **Massive:** Prevents ThreadPool locks. Enables the application to sustain massive dispatch peaks, operating at its "theoretical maximum rate" instead of failing catastrophically.

## 8. NativeAOT Impact
* **Positive:** `System.Threading.Channels` is a fully AOT-Friendly primitive as it resides in the core BCL (Base Class Library) and doesn't rely on reflection or dynamic compilation of logical blocks.

## 9. Maintainability Impact
* **Neutral:** Asynchronous code with Channels is idiomatic in C# 10+.

## 10. Developer Experience (DX) Impact
* **Transparent:** The user configures `MaxDegreeOfParallelism` and `ChannelCapacity` in `OutboxDispatcherOptions` from their `Program.cs`. The engine takes care of the rest.
