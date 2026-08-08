using System;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using EricksonLopez.Outbox.Serialization;
using EricksonLopez.Outbox.Retry;

namespace EricksonLopez.Outbox.Tests.Configuration;

public class OutboxOptionsConfigurationTests
{
    private readonly IServiceCollection _services = new ServiceCollection();
    private readonly OutboxOptions _sut;

    public OutboxOptionsConfigurationTests()
    {
        _sut = new OutboxOptions(_services);
    }

    [Fact]
    public void Constructor_NullServices_ThrowsArgumentNullException()
    {
        Action act = () => { _ = new OutboxOptions(null!); };
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void ConfigureRuntimeOptions_NullConfigure_ThrowsArgumentNullException()
    {
        Action act = () => _sut.ConfigureRuntimeOptions(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("configure");
    }

    [Fact]
    public void ConfigureRuntimeOptions_ValidConfigure_RegistersOptions()
    {
        _sut.ConfigureRuntimeOptions(opts => opts.SchemaName = "testschema");

        var serviceProvider = _services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<OutboxRuntimeOptions>>().Value;
        
        options.SchemaName.Should().Be("testschema");
    }

    [Fact]
    public void Configure_NullConfigure_ThrowsArgumentNullException()
    {
        Action act = () => _sut.Configure(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("configure");
    }

    [Fact]
    public void Configure_ValidConfigure_ExecutesDelegate()
    {
        var invoked = false;
        _sut.Configure(services => {
            services.Should().BeSameAs(_services);
            invoked = true;
        });

        invoked.Should().BeTrue();
    }

    [Fact]
    public void UseSerializer_InstanceNull_ThrowsArgumentNullException()
    {
        Action act = () => _sut.UseSerializer((IOutboxSerializer)null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("serializer");
    }

    [Fact]
    public void UseTypeResolver_NullResolver_ThrowsArgumentNullException()
    {
        Action act = () => _sut.UseTypeResolver(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("typeResolver");
    }

    [Fact]
    public void UseTypeResolver_ValidResolver_RegistersInstance()
    {
        var resolver = Substitute.For<IOutboxMessageTypeResolver>();
        _sut.UseTypeResolver(resolver);

        var serviceProvider = _services.BuildServiceProvider();
        var registeredResolver = serviceProvider.GetRequiredService<IOutboxMessageTypeResolver>();
        
        registeredResolver.Should().BeSameAs(resolver);
    }

    [Fact]
    public void Route_NullPublisherInstance_ThrowsArgumentNullException()
    {
        var routeBuilder = _sut.Route("test");
        Action act = () => routeBuilder.ToPublisher((IBrokerPublisher)null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("publisher");
    }

    [Fact]
    public void Route_NullPublisherFactory_ThrowsArgumentNullException()
    {
        var routeBuilder = _sut.Route("test");
        Action act = () => routeBuilder.ToPublisher((Func<IServiceProvider, IBrokerPublisher>)null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("factory");
    }

    [Fact]
    public void Route_ValidPublisherInstance_RegistersRoute()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        _sut.Route("alias1").ToPublisher(publisher);

        _sut.Routes.Should().ContainKey("alias1");
        var resolvedPublisher = _sut.Routes["alias1"](Substitute.For<IServiceProvider>());
        resolvedPublisher.Should().BeSameAs(publisher);
    }

    [Fact]
    public void Route_ValidPublisherFactory_RegistersRoute()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        _sut.Route("alias2").ToPublisher(sp => publisher);

        _sut.Routes.Should().ContainKey("alias2");
        var resolvedPublisher = _sut.Routes["alias2"](Substitute.For<IServiceProvider>());
        resolvedPublisher.Should().BeSameAs(publisher);
    }

    [Fact]
    public void UseBroker_FactoryNull_ThrowsArgumentNullException()
    {
        Action act = () => _sut.UseBroker((Func<IServiceProvider, IBrokerPublisher>)null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("factory");
    }

    [Fact]
    public void UseBroker_PublisherInstanceNull_ThrowsArgumentNullException()
    {
        Action act = () => _sut.UseBroker((IBrokerPublisher)null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("publisher");
    }

    [Fact]
    public void UseBroker_FactoryWithRetryPolicy_RegistersInterceptor()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var retryPolicy = new FixedDelayRetryPolicy(TimeSpan.FromSeconds(1), 1);
        
        _sut.UseBroker(sp => publisher, retryPolicy);

        _sut.DefaultPublisherFactory.Should().NotBeNull();
        
        // Mock ILogger
        _services.AddLogging();
        var sp = _services.BuildServiceProvider();
        var factoryResult = _sut.DefaultPublisherFactory!(sp);
        
        factoryResult.Should().BeOfType<RetryDispatcherInterceptor>();
    }

    [Fact]
    public void UseBroker_TypeWithRetryPolicy_RegistersInterceptor()
    {
        var retryPolicy = new FixedDelayRetryPolicy(TimeSpan.FromSeconds(1), 1);
        
        _sut.UseBroker<TestBroker>(retryPolicy);

        _sut.DefaultPublisherFactory.Should().NotBeNull();
        
        _services.AddLogging();
        _services.AddSingleton<TestBroker>();
        var sp = _services.BuildServiceProvider();
        var factoryResult = _sut.DefaultPublisherFactory!(sp);
        
        factoryResult.Should().BeOfType<RetryDispatcherInterceptor>();
    }

    [Fact]
    public void UseBroker_TypeWithoutRetryPolicy_RegistersDirectly()
    {
        _sut.UseBroker<TestBroker>();

        _sut.DefaultPublisherFactory.Should().NotBeNull();
        
        _services.AddSingleton<TestBroker>();
        var sp = _services.BuildServiceProvider();
        var factoryResult = _sut.DefaultPublisherFactory!(sp);
        
        factoryResult.Should().BeOfType<TestBroker>();
    }

    public class TestBroker : IBrokerPublisher
    {
        public System.Threading.Tasks.ValueTask<DispatchResult> PublishRawAsync(OutboxMessage message, MessageMetadata metadata, DispatchContext context)
            => new(DispatchResult.Ok());
    }
}

public class OutboxOptionsAutoPropertiesTests
{
    [Fact]
    public void OutboxDispatcherOptions_Properties_Work()
    {
        var options = new OutboxDispatcherOptions();
        
        options.HasOnlySingletonMiddlewares = true;
        options.HasOnlySingletonMiddlewares.Should().BeTrue();
        
        options.PollingInterval = TimeSpan.FromSeconds(1);
        options.PollingInterval.Should().Be(TimeSpan.FromSeconds(1));
        
        options.UseAdaptivePolling = false;
        options.UseAdaptivePolling.Should().BeFalse();
        
        options.BatchSize = 50;
        options.BatchSize.Should().Be(50);
        
        options.MaxDegreeOfParallelism = 4;
        options.MaxDegreeOfParallelism.Should().Be(4);
        
        options.MaxBatchesPerSecond = 10;
        options.MaxBatchesPerSecond.Should().Be(10);
        
        options.ChannelCapacity = 500;
        options.ChannelCapacity.Should().Be(500);
        
        options.MaxRetryCount = 5;
        options.MaxRetryCount.Should().Be(5);
        
        options.ReclaimTimeout = TimeSpan.FromMinutes(10);
        options.ReclaimTimeout.Should().Be(TimeSpan.FromMinutes(10));
        
        options.ReclaimInterval = TimeSpan.FromMinutes(2);
        options.ReclaimInterval.Should().Be(TimeSpan.FromMinutes(2));
        
        options.DbRetryMaxAttempts = 5;
        options.DbRetryMaxAttempts.Should().Be(5);
        
        options.DbRetryBaseDelayMs = 100;
        options.DbRetryBaseDelayMs.Should().Be(100);
        
        options.PendingCountRefreshInterval = TimeSpan.FromSeconds(60);
        options.PendingCountRefreshInterval.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void OutboxRuntimeOptions_Properties_Work()
    {
        var options = new OutboxRuntimeOptions();
        
        options.InstanceId = "test-id";
        options.InstanceId.Should().Be("test-id");
        
        options.SchemaName = "test-schema";
        options.SchemaName.Should().Be("test-schema");
        
        options.TableName = "test-table";
        options.TableName.Should().Be("test-table");
        
        options.MaxPayloadSizeInBytes = 100;
        options.MaxPayloadSizeInBytes.Should().Be(100);
        
        options.MaxHeaderSizeInBytes = 50;
        options.MaxHeaderSizeInBytes.Should().Be(50);
        
        options.ThrowOnUnregisteredType = true;
        options.ThrowOnUnregisteredType.Should().BeTrue();
        
        options.MaxMessageAge = TimeSpan.FromDays(10);
        options.MaxMessageAge.Should().Be(TimeSpan.FromDays(10));
        
        options.MaxBackoffSeconds = 600;
        options.MaxBackoffSeconds.Should().Be(600);
        
        options.LargeTableThreshold = 10000;
        options.LargeTableThreshold.Should().Be(10000);
        
        options.DeleteOnDispatch = false;
        options.DeleteOnDispatch.Should().BeFalse();
        
        options.MaxStoreRatePerSecond = 1000;
        options.MaxStoreRatePerSecond.Should().Be(1000);
        
        options.ReclaimBatchLimit = 5000;
        options.ReclaimBatchLimit.Should().Be(5000);
    }

    [Fact]
    public void OutboxInboxOptions_Properties_Work()
    {
        var options = new OutboxInboxOptions();
        
        options.RetentionPeriod = TimeSpan.FromDays(30);
        options.RetentionPeriod.Should().Be(TimeSpan.FromDays(30));
        
        options.DuplicateDetectionWindow = TimeSpan.FromHours(12);
        options.DuplicateDetectionWindow.Should().Be(TimeSpan.FromHours(12));
        
        options.CleanupInterval = TimeSpan.FromHours(2);
        options.CleanupInterval.Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public void OutboxHealthCheckOptions_Properties_Work()
    {
        var options = new OutboxHealthCheckOptions();
        
        options.WarningThreshold = 500;
        options.WarningThreshold.Should().Be(500);
    }

    [Fact]
    public void UseBroker_FactoryWithRetryPolicyAndNullCircuitBreaker_CreatesDefaultCircuitBreaker()
    {
        var services = new ServiceCollection();
        var options = new OutboxOptions(services);
        var publisher = Substitute.For<IBrokerPublisher>();
        var retryPolicy = new FixedDelayRetryPolicy(TimeSpan.FromSeconds(1), 1);
        
        options.UseBroker(sp => publisher, retryPolicy, null);
        
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        var factoryResult = options.DefaultPublisherFactory!(sp);
        
        var interceptor = factoryResult as RetryDispatcherInterceptor;
        interceptor.Should().NotBeNull();
        
        // Use reflection to verify the circuit breaker is not null
        var field = typeof(RetryDispatcherInterceptor).GetField("_circuitBreaker", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var cb = field!.GetValue(interceptor);
        cb.Should().NotBeNull();
        cb.Should().BeOfType<CircuitBreakerState>();
    }

    [Fact]
    public void UseBroker_InstanceWithRetryPolicyAndNullCircuitBreaker_CreatesDefaultCircuitBreaker()
    {
        var services = new ServiceCollection();
        var options = new OutboxOptions(services);
        var publisher = Substitute.For<IBrokerPublisher>();
        var retryPolicy = new FixedDelayRetryPolicy(TimeSpan.FromSeconds(1), 1);
        
        options.UseBroker(publisher, retryPolicy, null);
        
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        var factoryResult = options.DefaultPublisherFactory!(sp);
        
        var interceptor = factoryResult as RetryDispatcherInterceptor;
        interceptor.Should().NotBeNull();
        
        var field = typeof(RetryDispatcherInterceptor).GetField("_circuitBreaker", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var cb = field!.GetValue(interceptor);
        cb.Should().NotBeNull();
    }
}
