// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Retry;
using EricksonLopez.Outbox.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

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
    internal Dictionary<string, Func<IServiceProvider, IBrokerPublisher>> Routes { get; } = new(StringComparer.Ordinal);


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
    /// Configures the default broker publisher implementation for the outbox.
    /// </summary>
    /// <typeparam name="TBroker">The broker publisher implementation type.</typeparam>
    /// <param name="retryPolicy">An optional retry policy to wrap publishing attempts.</param>
    /// <param name="circuitBreaker">An optional circuit breaker state tracker.</param>
    /// <returns>The current <see cref="OutboxOptions"/> instance for method chaining.</returns>
    public OutboxOptions UseBroker<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TBroker>(RetryPolicy? retryPolicy = null, CircuitBreakerState? circuitBreaker = null) where TBroker : class, IBrokerPublisher
    {
        if (typeof(TBroker) != typeof(IBrokerPublisher))
        {
            Services.TryAddSingleton<TBroker>();
        }

        if (retryPolicy is null)
        {
            DefaultPublisherFactory = sp => sp.GetRequiredService<TBroker>();
            // Stryker disable once all 
            Services.TryAddSingleton<IBrokerPublisher>(sp => sp.GetRequiredService<TBroker>());
        }
        else
        {
            var cb = circuitBreaker ?? new CircuitBreakerState();
            DefaultPublisherFactory = sp => new RetryDispatcherInterceptor(
                sp.GetRequiredService<TBroker>(),
                retryPolicy,
                cb,
                sp.GetRequiredService<ILogger<RetryDispatcherInterceptor>>());
            // Stryker disable once all 
            Services.TryAddSingleton<IBrokerPublisher>(DefaultPublisherFactory);
        }
        return this;
    }

    /// <summary>
    /// Configures the default broker publisher factory delegate for the outbox.
    /// </summary>
    /// <param name="factory">A factory delegate that produces an <see cref="IBrokerPublisher"/> instance.</param>
    /// <param name="retryPolicy">An optional retry policy to wrap publishing attempts.</param>
    /// <param name="circuitBreaker">An optional circuit breaker state tracker.</param>
    /// <returns>The current <see cref="OutboxOptions"/> instance for method chaining.</returns>
    public OutboxOptions UseBroker(Func<IServiceProvider, IBrokerPublisher> factory, RetryPolicy? retryPolicy = null, CircuitBreakerState? circuitBreaker = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (retryPolicy is null)
        {
            DefaultPublisherFactory = factory;
            // Stryker disable once all 
            Services.TryAddSingleton(factory);
        }
        else
        {
            var cb = circuitBreaker ?? new CircuitBreakerState();
            DefaultPublisherFactory = sp => new RetryDispatcherInterceptor(
                factory(sp),
                retryPolicy,
                cb,
                sp.GetRequiredService<ILogger<RetryDispatcherInterceptor>>());
            // Stryker disable once all 
            Services.TryAddSingleton(DefaultPublisherFactory);
        }
        return this;
    }

    /// <summary>
    /// Configures the default broker publisher instance for the outbox.
    /// </summary>
    /// <param name="publisher">The singleton broker publisher instance to register.</param>
    /// <param name="retryPolicy">An optional retry policy to wrap publishing attempts.</param>
    /// <param name="circuitBreaker">An optional circuit breaker state tracker.</param>
    /// <returns>The current <see cref="OutboxOptions"/> instance for method chaining.</returns>
    public OutboxOptions UseBroker(IBrokerPublisher publisher, RetryPolicy? retryPolicy = null, CircuitBreakerState? circuitBreaker = null)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        if (retryPolicy is null)
        {
            DefaultPublisherFactory = _ => publisher;
            // Stryker disable once all 
            Services.TryAddSingleton(publisher);
        }
        else
        {
            var cb = circuitBreaker ?? new CircuitBreakerState();
            DefaultPublisherFactory = sp => new RetryDispatcherInterceptor(
                publisher,
                retryPolicy,
                cb,
                sp.GetRequiredService<ILogger<RetryDispatcherInterceptor>>());
            // Stryker disable once all 
            Services.TryAddSingleton(DefaultPublisherFactory);
        }
        return this;
    }

    /// <summary>
    /// Starts configuring a route for a specific message type.
    /// </summary>
    /// <param name="messageTypeAlias">The alias or name of the message type.</param>
    /// <returns>A builder to specify the publisher for this route.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="messageTypeAlias"/> is <see langword="null"/>.</exception>
    public BrokerRouteBuilder Route(string messageTypeAlias)
    {
        return new BrokerRouteBuilder(this, messageTypeAlias);
    }

    /// <summary>
    /// Starts configuring routes for multiple message types in bulk.
    /// </summary>
    /// <param name="messageTypeAliases">The message type aliases to route.</param>
    /// <returns>A builder to specify the publisher for this route group.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="messageTypeAliases"/> is <see langword="null"/>.</exception>
    public BrokerRouteGroupBuilder RouteGroup(params string[] messageTypeAliases)
    {
        ArgumentNullException.ThrowIfNull(messageTypeAliases);
        return new BrokerRouteGroupBuilder(this, messageTypeAliases);
    }

    /// <summary>
    /// Starts configuring routes for multiple message types in bulk.
    /// </summary>
    /// <param name="messageTypeAliases">The collection of message type aliases to route.</param>
    /// <returns>A builder to specify the publisher for this route group.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="messageTypeAliases"/> is <see langword="null"/>.</exception>
    // Stryker disable all 
    public BrokerRouteGroupBuilder RouteGroup(IEnumerable<string> messageTypeAliases)
    {
        // Stryker disable once all 
        ArgumentNullException.ThrowIfNull(messageTypeAliases);
        // Stryker disable once all 
        return new BrokerRouteGroupBuilder(this, new List<string>(messageTypeAliases));
    }
    // Stryker restore all
}



