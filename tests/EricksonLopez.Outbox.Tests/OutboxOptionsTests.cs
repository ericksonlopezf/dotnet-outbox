using EricksonLopez.Outbox.Hosting;
using System;
using System.Threading.Tasks;
using AwesomeAssertions;

using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Serialization;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;

namespace EricksonLopez.Outbox.Tests;

public class OutboxOptionsTests
{
    [Fact]
    public void Constructor_ShouldThrow_WhenServicesNull()
    {
        Action act = () => ((IServiceCollection)null!).AddOutbox(_ => { });
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Configure_ShouldNotThrow_WhenActionNull()
    {
        // AddOutbox accepts null configure (optional parameter) — it's valid to call with no config.
        var services = new ServiceCollection();
        Action act = () => services.AddOutbox(null!);
        act.Should().NotThrow();
    }

    [Fact]
    public void Configure_ShouldInvokeAction()
    {
        var services = new ServiceCollection();
        bool invoked = false;
        services.AddOutbox(opts => { invoked = true; });
        invoked.Should().BeTrue();
    }

    [Fact]
    public void UseSerializer_ShouldThrow_WhenSerializerNull()
    {
        var services = new ServiceCollection();
        Action act = () => services.AddOutbox(options => options.UseSerializer((IOutboxSerializer)null!));
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void UseSerializer_Should_Register_Serializer()
    {
        var services = new ServiceCollection();
        var serializer = Substitute.For<IOutboxSerializer>();
        services.AddOutbox(options => options.UseSerializer(serializer));
        var provider = services.BuildServiceProvider();
        provider.GetService<IOutboxSerializer>().Should().BeSameAs(serializer);
    }

    [Fact]
    public void UseSerializer_Generic_Should_Register_Serializer()
    {
        var services = new ServiceCollection();
        services.AddOutbox(options => options.UseSerializer<TestSerializer>());
        var provider = services.BuildServiceProvider();
        provider.GetService<IOutboxSerializer>().Should().BeOfType<TestSerializer>();
    }

    [Fact]
    public void UseBroker_Should_Register_Broker()
    {
        var services = new ServiceCollection();
        services.AddOutbox(options => options.UseBroker<TestBrokerPublisher>());
        var provider = services.BuildServiceProvider();
        provider.GetService<IBrokerPublisher>().Should().BeOfType<TestBrokerPublisher>();
    }

    [Fact]
    public void UseBroker_Generic_WithRetryPolicy_Should_Register_Interceptor()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOutbox(options => options.UseBroker<TestBrokerPublisher>(
            new EricksonLopez.Outbox.Retry.FixedDelayRetryPolicy(TimeSpan.FromSeconds(1), 3)));
        var provider = services.BuildServiceProvider();
        var publisher = provider.GetService<IBrokerPublisher>();
        publisher.Should().BeOfType<EricksonLopez.Outbox.Retry.RetryDispatcherInterceptor>();
    }

    [Fact]
    public void UseBroker_Factory_ShouldThrow_WhenFactoryNull()
    {
        var services = new ServiceCollection();
        Action act = () => services.AddOutbox(options =>
            options.UseBroker((Func<IServiceProvider, IBrokerPublisher>)null!));
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void UseBroker_Factory_WithoutRetryPolicy_Should_Register_Factory()
    {
        var services = new ServiceCollection();
        var instance = new TestBrokerPublisher();
        services.AddOutbox(options => options.UseBroker(_ => instance));
        var provider = services.BuildServiceProvider();
        provider.GetService<IBrokerPublisher>().Should().BeSameAs(instance);
    }

    [Fact]
    public void UseBroker_Factory_WithRetryPolicy_Should_Register_Interceptor()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var instance = new TestBrokerPublisher();
        services.AddOutbox(options => options.UseBroker(
            _ => instance,
            new EricksonLopez.Outbox.Retry.FixedDelayRetryPolicy(TimeSpan.FromSeconds(1), 3)));
        var provider = services.BuildServiceProvider();
        var publisher = provider.GetService<IBrokerPublisher>();
        publisher.Should().BeOfType<EricksonLopez.Outbox.Retry.RetryDispatcherInterceptor>();
    }

    [Fact]
    public void UseBroker_Instance_ShouldThrow_WhenInstanceNull()
    {
        var services = new ServiceCollection();
        Action act = () => services.AddOutbox(options => options.UseBroker((IBrokerPublisher)null!));
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void UseBroker_Instance_Should_Register_Instance()
    {
        var services = new ServiceCollection();
        var instance = new TestBrokerPublisher();
        services.AddOutbox(options => options.UseBroker(instance));
        var provider = services.BuildServiceProvider();
        provider.GetService<IBrokerPublisher>().Should().BeSameAs(instance);
    }

    [Fact]
    public void UseTypeResolver_ShouldThrow_WhenResolverNull()
    {
        var services = new ServiceCollection();
        Action act = () => services.AddOutbox(options => options.UseTypeResolver(null!));
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void UseTypeResolver_Should_Register_Resolver()
    {
        var services = new ServiceCollection();
        var resolver = Substitute.For<IOutboxMessageTypeResolver>();
        services.AddOutbox(options => options.UseTypeResolver(resolver));
        var provider = services.BuildServiceProvider();
        provider.GetService<IOutboxMessageTypeResolver>().Should().BeSameAs(resolver);
    }

    public class TestSerializer : IOutboxSerializer
    {
        public ReadOnlyMemory<byte> Serialize<TMessage>(TMessage message) => default;
        public TMessage Deserialize<TMessage>(ReadOnlySpan<byte> data) => default!;
    }

    [Fact]
    public void UseBroker_With_Custom_CircuitBreaker_Should_Use_It()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var cb = new EricksonLopez.Outbox.Retry.CircuitBreakerState();
        services.AddOutbox(options => options.UseBroker<TestPublisher>(
            retryPolicy: EricksonLopez.Outbox.Retry.RetryPolicy.Default,
            circuitBreaker: cb));
        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<EricksonLopez.Outbox.IBrokerPublisher>();
        publisher.Should().BeOfType<EricksonLopez.Outbox.Retry.RetryDispatcherInterceptor>();
    }

    [Fact]
    public void UseBrokerFactory_With_Custom_CircuitBreaker_Should_Use_It()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var cb = new EricksonLopez.Outbox.Retry.CircuitBreakerState();
        services.AddOutbox(options => options.UseBroker(
            _ => new TestPublisher(),
            retryPolicy: EricksonLopez.Outbox.Retry.RetryPolicy.Default,
            circuitBreaker: cb));
        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<EricksonLopez.Outbox.IBrokerPublisher>();
        publisher.Should().BeOfType<EricksonLopez.Outbox.Retry.RetryDispatcherInterceptor>();
    }

    private sealed class TestPublisher : IBrokerPublisher
    {
        public System.Threading.Tasks.ValueTask<DispatchResult> PublishRawAsync(OutboxMessage message, MessageMetadata metadata, DispatchContext context)
            => new(DispatchResult.Ok());
        
        
    }

    public class TestBrokerPublisher : IBrokerPublisher
    {
        public System.Threading.Tasks.ValueTask<DispatchResult> PublishRawAsync(OutboxMessage message, MessageMetadata metadata, DispatchContext context)
            => new(DispatchResult.Ok());
        
    }

    [Fact]
    public void UseBroker_Generic_WithoutRetryPolicy_Should_Register_Type()
    {
        var services = new ServiceCollection();
        services.AddOutbox(options => options.UseBroker<TestBrokerPublisher>(retryPolicy: null));
        var provider = services.BuildServiceProvider();
        var publisher = provider.GetService<IBrokerPublisher>();
        publisher.Should().BeOfType<TestBrokerPublisher>();
    }

    [Fact]
    public void UseBroker_Instance_WithRetryPolicy_Should_Register_Interceptor()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var instance = new TestBrokerPublisher();
        services.AddOutbox(options => options.UseBroker(instance, new EricksonLopez.Outbox.Retry.FixedDelayRetryPolicy(TimeSpan.FromSeconds(1), 3)));
        var provider = services.BuildServiceProvider();
        var publisher = provider.GetService<IBrokerPublisher>();
        publisher.Should().BeOfType<EricksonLopez.Outbox.Retry.RetryDispatcherInterceptor>();
    }
}


