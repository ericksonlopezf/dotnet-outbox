// Stryker disable all : Covered by ADR-013. Edge cases, micro-optimizations, logging, and validation strings are not rigorously mutated.
using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Serialization;

namespace EricksonLopez.Outbox;

/// <summary>
/// Provides a fluent builder API for configuring the EricksonLopez.Outbox core services.
/// </summary>
/// <remarks>
/// Fluent API usage:
/// <code>
///   services.AddOutbox(options =>
///   {
///       options.UseSerializer(new NativeAotJsonSerializer(MyGeneratedContext.Default));
///       options.UseBroker&lt;RabbitMQBrokerPublisher&gt;();
///   });
/// </code>
/// </remarks>
public sealed class OutboxOptions
{
    // FIX-06: Made internal to enforce encapsulation.
    //
    // Root cause: exposing IServiceCollection publicly allowed callers to bypass
    // the OutboxOptions fluent API by doing options.Services.AddScoped<AnythingRandom>()
    // which couples the outbox configuration surface to the raw DI container.
    //
    // Fix: Make Services internal. All legitimate registration paths go through the
    // strongly-typed methods: UseSerializer(), UseTypeResolver(), UseBroker().
    //
    // Third-party integration libraries that need to register additional services
    // during outbox configuration should use the Configure() extension point below.
    internal IServiceCollection Services { get; init; } = null!;
    internal Func<IServiceProvider, IBrokerPublisher>? DefaultPublisherFactory { get; set; }
    internal System.Collections.Generic.Dictionary<string, Func<IServiceProvider, IBrokerPublisher>> Routes { get; } = new(StringComparer.Ordinal);


    internal OutboxOptions(IServiceCollection services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>
    /// Configures the runtime behavior options for the outbox.
    /// </summary>
    /// <param name="configure">The delegate used to configure the runtime options.</param>
    /// <returns>The current <see cref="OutboxOptions"/> instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    public OutboxOptions ConfigureRuntimeOptions(Action<OutboxRuntimeOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Services.Configure(configure);
        return this;
    }

    /// <summary>
    /// Exposes an extension point for first-party storage engines and brokers to register additional
    /// dependency injection services during outbox configuration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>WARNING:</b> Exposes the underlying <see cref="IServiceCollection"/>.
    /// End users should avoid using it directly to prevent accidental tight coupling 
    /// to the dependency injection framework's internals.
    /// </para>
    /// <para>
    /// <b>ROADMAP-v2 — Encapsulation:</b><br/>
    /// Exposing <see cref="IServiceCollection"/> directly (even behind <c>[EditorBrowsable(Advanced)]</c>)
    /// creates an encapsulation gap: third-party integrations could register conflicting singleton services,
    /// override internal registrations, or introduce lifetime mismatches that are silent until runtime.
    /// In v2.0, consider a restricted <c>OutboxServicesBuilder</c> that exposes only approved extension points:
    /// <code>
    /// public OutboxOptions ConfigureServices(Action&lt;OutboxServicesBuilder&gt; configure)
    /// </code>
    /// This is deferred to avoid a breaking change in the public API surface.
    /// </para>
    /// </remarks>
    /// <param name="configure">The delegate that registers additional services into the underlying collection.</param>
    /// <returns>The current <see cref="OutboxOptions"/> instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Advanced)]
    public OutboxOptions Configure(Action<IServiceCollection> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(Services);
        return this;
    }


    /// <summary>
    /// Registers a specific serializer instance for use by the outbox.
    /// </summary>
    /// <remarks>
    /// Required for NativeAOT compatibility — must be a source-generated serializer.
    /// </remarks>
    /// <param name="serializer">The singleton serializer instance to register.</param>
    /// <returns>The current <see cref="OutboxOptions"/> instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="serializer"/> is <see langword="null"/>.</exception>
    public OutboxOptions UseSerializer(IOutboxSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        // Register under the interface — concrete type registration would break IOutboxSerializer injection in DefaultOutbox.
        Services.TryAddSingleton<IOutboxSerializer>(serializer);
        return this;
    }

    /// <summary>
    /// Registers a specific serializer type for use by the outbox.
    /// </summary>
    /// <typeparam name="TSerializer">The type of the serializer to register.</typeparam>
    /// <returns>The current <see cref="OutboxOptions"/> instance for method chaining.</returns>
    public OutboxOptions UseSerializer<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] TSerializer>() where TSerializer : class, IOutboxSerializer
    {
        Services.TryAddSingleton<IOutboxSerializer, TSerializer>();
        return this;
    }

    /// <summary>
    /// Registers a specific message type resolver instance for use by the outbox.
    /// </summary>
    /// <param name="typeResolver">The singleton type resolver instance to register.</param>
    /// <returns>The current <see cref="OutboxOptions"/> instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="typeResolver"/> is <see langword="null"/>.</exception>
    public OutboxOptions UseTypeResolver(IOutboxMessageTypeResolver typeResolver)
    {
        ArgumentNullException.ThrowIfNull(typeResolver);
        // Register under the interface — concrete type registration would break IOutboxMessageTypeResolver
        // injection in DefaultOutbox (same bug that was fixed in UseSerializer in P0-Fix #5).
        Services.TryAddSingleton<IOutboxMessageTypeResolver>(typeResolver);
        return this;
    }

    /// <summary>
    /// Starts configuring a route for a specific message type.
    /// </summary>
    /// <param name="messageTypeAlias">The alias or name of the message type.</param>
    /// <returns>A builder to specify the publisher for this route.</returns>
    public BrokerRouteBuilder Route(string messageTypeAlias)
    {
        return new BrokerRouteBuilder(this, messageTypeAlias);
    }

    /// <summary>
    /// Configures a message-type-specific route to a designated broker publisher.
    /// </summary>
    /// <remarks>
    /// Obtained via <see cref="OutboxOptions.Route"/> and completed with
    /// <see cref="ToPublisher(IBrokerPublisher)"/> or <see cref="ToPublisher(Func{IServiceProvider, IBrokerPublisher})"/>.
    /// </remarks>
    public sealed class BrokerRouteBuilder
    {
        private readonly OutboxOptions _options;
        private readonly string _messageTypeAlias;

        internal BrokerRouteBuilder(OutboxOptions options, string messageTypeAlias)
        {
            _options = options;
            _messageTypeAlias = messageTypeAlias;
        }

        /// <summary>
        /// Routes the specified message type to the given singleton publisher instance.
        /// </summary>
        /// <param name="publisher">The publisher instance to dispatch messages of the configured type to.</param>
        /// <returns>The parent <see cref="OutboxOptions"/> instance for method chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="publisher"/> is <see langword="null"/>.</exception>
        public OutboxOptions ToPublisher(IBrokerPublisher publisher)
        {
            ArgumentNullException.ThrowIfNull(publisher);
            _options.Routes[_messageTypeAlias] = _ => publisher;
            return _options;
        }

        /// <summary>
        /// Routes the specified message type to a publisher resolved via the provided factory delegate.
        /// </summary>
        /// <param name="factory">A factory delegate that receives the <see cref="IServiceProvider"/> and returns the publisher to use.</param>
        /// <returns>The parent <see cref="OutboxOptions"/> instance for method chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
        public OutboxOptions ToPublisher(Func<IServiceProvider, IBrokerPublisher> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            _options.Routes[_messageTypeAlias] = factory;
            return _options;
        }

    }

    /// <summary>
    /// Registers a broker publisher using a custom factory delegate.
    /// </summary>
    /// <remarks>
    /// Use this overload when the publisher requires custom construction logic or for NativeAOT compatibility.
    /// </remarks>
    /// <param name="factory">The delegate used to construct the broker publisher.</param>
    /// <param name="retryPolicy">The optional retry policy to wrap the publisher with.</param>
    /// <param name="circuitBreaker">The optional circuit breaker state to associate with the retry policy.</param>
    /// <returns>The current <see cref="OutboxOptions"/> instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    public OutboxOptions UseBroker(
        Func<IServiceProvider, IBrokerPublisher> factory,
        EricksonLopez.Outbox.Retry.RetryPolicy? retryPolicy = null,
        EricksonLopez.Outbox.Retry.CircuitBreakerState? circuitBreaker = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        
        if (retryPolicy != null)
        {
            var cb = circuitBreaker ?? new EricksonLopez.Outbox.Retry.CircuitBreakerState();
            DefaultPublisherFactory = sp => 
                new EricksonLopez.Outbox.Retry.RetryDispatcherInterceptor(
                    factory(sp), 
                    retryPolicy, 
                    cb, 
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<EricksonLopez.Outbox.Retry.RetryDispatcherInterceptor>>());
        }
        else
        {
            DefaultPublisherFactory = factory;
        }
        
        return this;
    }

    /// <summary>
    /// Registers a specific broker publisher instance.
    /// </summary>
    /// <remarks>
    /// This overload is useful in testing scenarios or when providing pre-built instances.
    /// </remarks>
    /// <param name="publisher">The singleton broker publisher instance to register.</param>
    /// <param name="retryPolicy">The optional retry policy to wrap the publisher with.</param>
    /// <param name="circuitBreaker">The optional circuit breaker state to associate with the retry policy.</param>
    /// <returns>The current <see cref="OutboxOptions"/> instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="publisher"/> is <see langword="null"/>.</exception>
    public OutboxOptions UseBroker(
        IBrokerPublisher publisher,
        EricksonLopez.Outbox.Retry.RetryPolicy? retryPolicy = null,
        EricksonLopez.Outbox.Retry.CircuitBreakerState? circuitBreaker = null)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        return UseBroker(_ => publisher, retryPolicy, circuitBreaker);
    }

    /// <summary>
    /// Registers a broker publisher type.
    /// </summary>
    /// <typeparam name="TBroker">The type of the broker publisher to register.</typeparam>
    /// <param name="retryPolicy">The optional retry policy to wrap the publisher with.</param>
    /// <param name="circuitBreaker">The optional circuit breaker state to associate with the retry policy.</param>
    /// <returns>The current <see cref="OutboxOptions"/> instance for method chaining.</returns>
    public OutboxOptions UseBroker<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] TBroker>(
        EricksonLopez.Outbox.Retry.RetryPolicy? retryPolicy = null,
        EricksonLopez.Outbox.Retry.CircuitBreakerState? circuitBreaker = null) where TBroker : class, IBrokerPublisher
    {
        Services.TryAddSingleton<TBroker>();
        
        if (retryPolicy != null)
        {
            var cb = circuitBreaker ?? new EricksonLopez.Outbox.Retry.CircuitBreakerState();
            DefaultPublisherFactory = sp => 
                new EricksonLopez.Outbox.Retry.RetryDispatcherInterceptor(
                    sp.GetRequiredService<TBroker>(), 
                    retryPolicy, 
                    cb, 
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<EricksonLopez.Outbox.Retry.RetryDispatcherInterceptor>>());
        }
        else
        {
            DefaultPublisherFactory = sp => sp.GetRequiredService<TBroker>();
        }
        
        return this;
    }
}

/// <summary>
/// Represents the configuration options for the Outbox Dispatcher background service.
/// </summary>
public sealed class OutboxDispatcherOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether all registered pipeline middlewares are registered as singletons.
    /// When <see langword="true"/>, the dispatcher caches the built middleware pipeline to avoid per-batch allocations.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool HasOnlySingletonMiddlewares { get; set; }
    /// <summary>
    /// Gets or sets the fixed time interval to wait between polling cycles when the outbox is empty.
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Gets or sets a value indicating whether the dispatcher should dynamically adjust the polling interval based on load.
    /// </summary>
    public bool UseAdaptivePolling { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of messages retrieved from the database per polling cycle.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Gets or sets the maximum number of concurrent consumer tasks draining the dispatcher channel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>ISSUE-CC3: Not yet implemented.</b> This property is reserved for v2.0.
    /// The current dispatcher processes messages in a single sequential loop per
    /// consumer goroutine. Setting this value has no effect in the current version.
    /// </para>
    /// <para>
    /// The default value of <c>min(ProcessorCount, 8)</c> is intentionally set for the
    /// planned parallel implementation. Do not rely on this value for throttling.
    /// </para>
    /// </remarks>
    public int MaxDegreeOfParallelism { get; set; } = Math.Min(Environment.ProcessorCount, 8);

    /// <summary>
    /// Gets or sets the maximum number of batches to process per second when adaptive polling is draining a large backlog.
    /// A value of 0 means no limit (unbounded).
    /// </summary>
    /// <remarks>
    /// This provides coarse-grained rate limiting on the outbox dispatcher.
    /// For granular per-message-type rate limiting, implement a custom <see cref="EricksonLopez.Outbox.Pipeline.IOutboxMiddleware"/>.
    /// </remarks>
    public int MaxBatchesPerSecond { get; set; }

    /// <summary>
    /// Gets or sets the maximum capacity of the in-memory channel connecting the poller to consumers.
    /// </summary>
    /// <remarks>
    /// Memory impact: each <see cref="EricksonLopez.Outbox.OutboxMessage"/> reference is ~24 bytes (pointer + header);
    /// the message body size is determined by the payload size in the database.
    /// With <c>ChannelCapacity = 1000</c> the channel itself consumes roughly 24 KB of heap.
    /// When the broker is unavailable and the channel reaches capacity, the poller blocks on
    /// <c>WriteAsync</c>, providing natural back-pressure against excessive DB polling.
    /// </remarks>
    public int ChannelCapacity { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the maximum number of retry attempts for a failed message dispatch before marking it as permanently failed.
    /// </summary>
    public int MaxRetryCount { get; set; } = 10;

    /// <summary>
    /// Gets or sets the timeout duration after which a stuck message is considered stale and available for reclamation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A message that has been claimed (state=1 InFlight) but not yet acknowledged as dispatched
    /// or failed is considered stale when its <c>updated_at</c> timestamp is older than
    /// <c>UtcNow - ReclaimTimeout</c>. The reclaim process resets its state to 0 (Pending),
    /// making it available for re-dispatch by any dispatcher instance.
    /// </para>
    /// <para>
    /// <b>AUDIT-FIX P1-D — Minimum value warning:</b><br/>
    /// <c>ReclaimTimeout</c> MUST be greater than the maximum time any single message can spend
    /// in the dispatch pipeline. Calculate your minimum as:
    /// <code>
    /// MinReclaimTimeout = MaxRetryCount × (MaxRetryDelay + BrokerCallTimeout) + buffer
    /// </code>
    /// With defaults (<see cref="MaxRetryCount"/> = 10, exponential backoff capped at ~5.7 hours)
    /// the theoretical worst-case dispatch time is several hours — but realistic values with
    /// typical broker timeouts are 3–15 minutes.
    /// </para>
    /// <para>
    /// <b>Too short</b>: a live dispatcher instance has its in-flight messages reclaimed by another
    /// instance or by the next polling cycle, causing duplicate delivery <em>beyond</em> normal
    /// At-Least-Once semantics. This is silent and hard to diagnose.
    /// </para>
    /// <para>
    /// <b>Too long</b>: after a dispatcher crash, messages remain stuck in state=1 longer than
    /// necessary, increasing observable latency until they are reclaimed.
    /// </para>
    /// <para>
    /// Default: 5 minutes — appropriate when broker calls are expected to complete within 1–2 minutes.
    /// Increase to 15–30 minutes for brokers with long retry schedules or high-latency networks.
    /// </para>
    /// </remarks>
    public TimeSpan ReclaimTimeout { get; set; } = TimeSpan.FromMinutes(5);


    /// <summary>
    /// Gets or sets the interval between background checks for stale messages to reclaim.
    /// </summary>
    public TimeSpan ReclaimInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets the maximum number of retry attempts for a transient DB operation failure
    /// (e.g., <c>MarkAsDispatchedAsync</c> or <c>MarkAsFailedAsync</c>) before the exception propagates.
    /// </summary>
    /// <remarks>
    /// These retries protect against transient DB connectivity blips during batch acknowledgement.
    /// The delay between attempts uses exponential backoff with ±25% jitter:
    /// <c>DbRetryBaseDelayMs × 2^(attempt-1)</c>, preventing synchronized storm recovery across
    /// concurrent consumers.
    /// Increase this value in environments with high DB round-trip latency (&gt; 100 ms).
    /// Default: 3 attempts.
    /// </remarks>
    public int DbRetryMaxAttempts { get; set; } = 3;

    /// <summary>
    /// Gets or sets the base delay in milliseconds between DB operation retry attempts.
    /// </summary>
    /// <remarks>
    /// The actual delay for attempt <c>n</c> is <c>DbRetryBaseDelayMs × 2^(n-1)</c> with ±25% jitter.
    /// With defaults (3 attempts, 50 ms base), the retry schedule is approximately:
    /// ~50 ms, ~100 ms, ~200 ms (before jitter).
    /// Default: 50 ms.
    /// </remarks>
    public int DbRetryBaseDelayMs { get; set; } = 50;

    /// <summary>
    /// Gets or sets the interval at which the approximate pending message count is refreshed
    /// and emitted as the <c>messaging.outbox.pending.messages</c> OpenTelemetry gauge.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Shorter intervals provide more up-to-date visibility into backlog growth but increase
    /// the number of <c>GetPendingCountAsync</c> database queries per unit time.
    /// For high-throughput systems (>10k msgs/s) with strict SLA monitoring, consider
    /// reducing this to 5–10 seconds.
    /// </para>
    /// <para>
    /// Default: 30 seconds — appropriate for most production workloads.
    /// </para>
    /// </remarks>
    public TimeSpan PendingCountRefreshInterval { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Represents the runtime configuration options governing message processing in the outbox.
/// </summary>
public sealed class OutboxRuntimeOptions
{
    /// <summary>
    /// Gets the unique identifier for this specific instance of the outbox runtime.
    /// </summary>
    public string InstanceId { get; internal set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets or sets the database schema name where the outbox tables reside.
    /// </summary>
    public string SchemaName { get; set; } = "outbox";

    /// <summary>
    /// Gets or sets the base name of the outbox messages table.
    /// </summary>
    public string TableName { get; set; } = "messages";

    /// <summary>
    /// Gets or sets the maximum allowed size, in bytes, for an individual message payload.
    /// </summary>
    public int MaxPayloadSizeInBytes { get; set; } = 1024 * 1024;

    /// <summary>
    /// Gets or sets the maximum allowed size, in bytes, for the serialized metadata headers of a message.
    /// </summary>
    public int MaxHeaderSizeInBytes { get; set; } = 64 * 1024;

    /// <summary>
    /// Gets or sets a value indicating whether to throw an exception if an unregistered message type is encountered.
    /// </summary>
    public bool ThrowOnUnregisteredType { get; set; }

    /// <summary>
    /// Gets or sets the maximum age of a message before it is eligible for cleanup or archival.
    /// </summary>
    /// <remarks>
    /// <b>Scheduling edge case:</b> This value also acts as an upper bound on how far in the future
    /// a message can be scheduled via <c>deliver_at</c>. If a message is stored with
    /// <c>deliver_at = NOW() + X days</c> and <c>MaxMessageAge &lt; X days</c>, the message will be
    /// silently excluded from polling (the <c>created_at</c> guard in <c>FetchPendingAsync</c> fires first)
    /// and will never be delivered.
    /// <para>
    /// Rule of thumb: set <c>MaxMessageAge</c> to at least <c>1 day + maximum deliver_at offset</c>.
    /// Example: if you schedule messages up to 7 days in the future, set <c>MaxMessageAge ≥ 8 days</c>.
    /// </para>
    /// </remarks>
    public TimeSpan MaxMessageAge { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Gets or sets the maximum exponential backoff delay, in seconds, for failed messages awaiting retry.
    /// </summary>
    /// <remarks>
    /// Caps the POWER(2, retry_count) * 10 formula used in the MarkFailed SQL.
    /// Default: 3600 (1 hour). Tune lower for time-sensitive messages.
    /// </remarks>
    public int MaxBackoffSeconds { get; set; } = 3600;

    /// <summary>
    /// Gets or sets the threshold (number of estimated rows) above which exact COUNT(*) is bypassed in favor of catalog estimates.
    /// Used by implementations like PostgreSQL for optimized pending count retrieval.
    /// </summary>
    public int LargeTableThreshold { get; set; } = 50_000;

    /// <summary>
    /// Gets or sets a value indicating whether dispatched messages are physically deleted from the outbox table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Default: <c>true</c></b> — Dispatched messages are DELETEd immediately after successful broker confirmation.
    /// This is the recommended setting for production workloads as it prevents MVCC bloat in PostgreSQL and keeps
    /// the outbox table compact for efficient SKIP LOCKED polling.
    /// </para>
    /// <para>
    /// <b>When set to <c>false</c></b>: Dispatched messages are UPDATEd to <c>state=2</c> (Dispatched) with
    /// <c>processed_at = NOW()</c> instead of being deleted. This mode enables:
    /// <list type="bullet">
    ///   <item>Post-mortem debugging (inspect which messages were dispatched and when)</item>
    ///   <item>Compliance / audit trails requiring message retention</item>
    ///   <item>Replay scenarios where re-dispatching from the outbox table is needed</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>WARNING:</b> Disabling delete-on-dispatch will cause the outbox table to grow indefinitely.
    /// You MUST implement a periodic cleanup job (e.g., <c>DELETE FROM outbox.messages WHERE state = 2 AND processed_at &lt; NOW() - INTERVAL '7 days'</c>)
    /// to prevent table bloat and index degradation.
    /// </para>
    /// </remarks>
    public bool DeleteOnDispatch { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of messages that can be stored per second via <c>IOutbox.StoreAsync</c>.
    /// A value of <c>0</c> means no limit (unbounded).
    /// </summary>
    /// <remarks>
    /// <para>
    /// P3-9 AUDIT FIX: Rate limiting on the store path prevents a malicious or buggy producer from
    /// flooding the outbox table. This is distinct from <see cref="OutboxDispatcherOptions.MaxBatchesPerSecond"/>
    /// which only limits the dispatcher polling rate.
    /// </para>
    /// <para>
    /// Default: <c>0</c> (no limit). Set to a positive value to enable rate limiting.
    /// Exceeding the limit throws <see cref="System.InvalidOperationException"/>.
    /// </para>
    /// </remarks>
    public int MaxStoreRatePerSecond { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of stale messages (state=1 InFlight) reclaimed per reclaim cycle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ISSUE-SQL3 FIX: Previously hardcoded to 1000 in the SQL. This option allows tuning the reclaim
    /// batch size independently of the dispatch batch size (<see cref="OutboxDispatcherOptions.BatchSize"/>).
    /// </para>
    /// <para>
    /// In normal operation the value of 1000 is more than sufficient. In environments with cascading
    /// crash scenarios (e.g., deployment rollback with hundreds of workers crashing simultaneously),
    /// raising this to 5000–10000 allows faster drain of the InFlight backlog on restart.
    /// </para>
    /// <para>
    /// Default: <c>1000</c>. Must be a positive integer.
    /// </para>
    /// </remarks>
    public int ReclaimBatchLimit { get; set; } = 1000;

    /// <summary>
    /// Gets or sets a value indicating whether to include the <c>messaging.message.type</c> tag
    /// on OpenTelemetry metrics instruments (counters, histograms).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Default: <c>true</c></b> — message type tags are emitted on all OTel metrics for
    /// per-type observability (e.g., alert on a specific message type failing).
    /// </para>
    /// <para>
    /// <b>When set to <c>false</c></b>: The <c>messaging.message.type</c> tag dimension is
    /// omitted from all metric instruments. Use this in environments with many distinct message
    /// types (high cardinality) that would cause metric series explosion in your observability
    /// backend (Prometheus, Datadog, etc.).
    /// </para>
    /// <para>
    /// This setting does <b>not</b> affect tracing (Activity tags) — type information is
    /// always included in spans regardless of this setting.
    /// </para>
    /// </remarks>
    // AUDIT-FIX G2: OTel metric tag cardinality opt-out. When an application has hundreds of
    // distinct message types, emitting message_type as a metric dimension causes series explosion
    // in backends like Prometheus (O(N) series per metric * N message types). Setting this to
    // false collapses all message types into a single dimension, trading observability granularity
    // for metric scalability. Tracing is unaffected — type information is always in spans.
    public bool IncludeMessageTypeTag { get; set; } = true;
}

/// <summary>
/// Represents the configuration options for the Inbox idempotency system.
/// </summary>
public sealed class OutboxInboxOptions
{
    /// <summary>
    /// Gets or sets the duration for which processed idempotency records are retained before being purged.
    /// </summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Gets or sets the time window within which duplicate messages are detected.
    /// </summary>
    public TimeSpan DuplicateDetectionWindow { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Gets or sets the interval between background cleanup operations for expired idempotency records.
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>
/// Represents the configuration options for the Outbox Health Check service.
/// </summary>
public sealed class OutboxHealthCheckOptions
{
    /// <summary>
    /// Gets or sets the threshold of pending messages above which the health check reports a degraded status.
    /// </summary>
    /// <remarks>
    /// Default: 1000. Tune based on your expected throughput and SLA.
    /// </remarks>
    public int WarningThreshold { get; set; } = 1_000;
}
