// Copyright © Erickson Lopez. MIT License.
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox.Retry;
using EricksonLopez.Outbox.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

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
    public void UseSerializer_Instance_RegistersSingleton()
    {
        var serializer = Substitute.For<IOutboxSerializer>();
        _sut.UseSerializer(serializer);

        var sp = _services.BuildServiceProvider();
        sp.GetRequiredService<IOutboxSerializer>().Should().BeSameAs(serializer);
    }

    [Fact]
    public void UseSerializer_Generic_RegistersSingleton()
    {
        _sut.UseSerializer<TestCustomSerializer>();

        var sp = _services.BuildServiceProvider();
        sp.GetRequiredService<IOutboxSerializer>().Should().BeOfType<TestCustomSerializer>();
    }

    private sealed class TestCustomSerializer : IOutboxSerializer
    {
        public ReadOnlyMemory<byte> Serialize<TMessage>(TMessage message) => default;
        public TMessage Deserialize<TMessage>(ReadOnlySpan<byte> data) => default!;
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
    public void RouteGroup_NullAliases_ThrowsArgumentNullException()
    {
        Action act = () => _sut.RouteGroup(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("messageTypeAliases");
    }

    [Fact]
    public void RouteGroup_NullPublisherInstance_ThrowsArgumentNullException()
    {
        var groupBuilder = _sut.RouteGroup("typeA", "typeB");
        Action act = () => groupBuilder.ToPublisher((IBrokerPublisher)null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("publisher");
    }

    [Fact]
    public void RouteGroup_NullPublisherFactory_ThrowsArgumentNullException()
    {
        var groupBuilder = _sut.RouteGroup("typeA", "typeB");
        Action act = () => groupBuilder.ToPublisher((Func<IServiceProvider, IBrokerPublisher>)null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("factory");
    }

    [Fact]
    public void RouteGroup_ValidPublisherInstance_RegistersAllRoutes()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        _sut.RouteGroup("typeA", "typeB").ToPublisher(publisher);

        _sut.Routes.Should().ContainKey("typeA");
        _sut.Routes.Should().ContainKey("typeB");
        _sut.Routes["typeA"](Substitute.For<IServiceProvider>()).Should().BeSameAs(publisher);
        _sut.Routes["typeB"](Substitute.For<IServiceProvider>()).Should().BeSameAs(publisher);
    }

    [Fact]
    public void RouteGroup_ValidPublisherFactory_RegistersAllRoutes()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        _sut.RouteGroup("typeC", "typeD").ToPublisher(sp => publisher);

        _sut.Routes.Should().ContainKey("typeC");
        _sut.Routes.Should().ContainKey("typeD");
        _sut.Routes["typeC"](Substitute.For<IServiceProvider>()).Should().BeSameAs(publisher);
        _sut.Routes["typeD"](Substitute.For<IServiceProvider>()).Should().BeSameAs(publisher);
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
        
        _services.AddLogging();
        var sp = _services.BuildServiceProvider();
        var factoryResult = _sut.DefaultPublisherFactory!(sp);
        
        factoryResult.Should().BeOfType<RetryDispatcherInterceptor>();
    }

    [Fact]
    public void UseBroker_FactoryWithCustomCircuitBreaker_PassesCustomInstance()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var retryPolicy = new FixedDelayRetryPolicy(TimeSpan.FromSeconds(1), 1);
        var customCb = new CircuitBreakerState();
        
        _sut.UseBroker(sp => publisher, retryPolicy, customCb);

        _services.AddLogging();
        var sp = _services.BuildServiceProvider();
        var factoryResult = _sut.DefaultPublisherFactory!(sp);
        
        var interceptor = factoryResult as RetryDispatcherInterceptor;
        interceptor.Should().NotBeNull();
        interceptor!.CircuitBreaker.Should().BeSameAs(customCb);
    }

    [Fact]
    public void UseBroker_TypeWithRetryPolicy_RegistersInterceptorAndSelfInServices()
    {
        var retryPolicy = new FixedDelayRetryPolicy(TimeSpan.FromSeconds(1), 1);
        
        _sut.UseBroker<TestBroker>(retryPolicy);

        _sut.DefaultPublisherFactory.Should().NotBeNull();
        
        _services.AddLogging();
        // Do NOT manually add TestBroker to services, verify UseBroker<TestBroker> added it!
        var sp = _services.BuildServiceProvider();
        var factoryResult = _sut.DefaultPublisherFactory!(sp);
        
        factoryResult.Should().BeOfType<RetryDispatcherInterceptor>();
    }

    [Fact]
    public void UseBroker_TypeWithCustomCircuitBreaker_PassesCustomInstance()
    {
        var retryPolicy = new FixedDelayRetryPolicy(TimeSpan.FromSeconds(1), 1);
        var customCb = new CircuitBreakerState();
        
        _sut.UseBroker<TestBroker>(retryPolicy, customCb);

        _services.AddLogging();
        var sp = _services.BuildServiceProvider();
        var factoryResult = _sut.DefaultPublisherFactory!(sp);
        
        var interceptor = factoryResult as RetryDispatcherInterceptor;
        interceptor.Should().NotBeNull();
        interceptor!.CircuitBreaker.Should().BeSameAs(customCb);
    }

    [Fact]
    public void UseBroker_TypeWithoutRetryPolicy_RegistersDirectly()
    {
        _sut.UseBroker<TestBroker>();

        _sut.DefaultPublisherFactory.Should().NotBeNull();
        
        // Do NOT manually add TestBroker to services, verify UseBroker<TestBroker> added it!
        var sp = _services.BuildServiceProvider();
        var factoryResult = _sut.DefaultPublisherFactory!(sp);
        
        factoryResult.Should().BeOfType<TestBroker>();
    }

    public class TestBroker : IBrokerPublisher
    {
        public ValueTask<DispatchResult> PublishRawAsync(OutboxMessage message, OutboxMessageMetadata metadata, DispatchContext context)
            => new(DispatchResult.Ok());
    }
}

public class OutboxOptionsAutoPropertiesTests
{
    [Fact]
    public void OutboxDispatcherOptions_Defaults_AreCorrect()
    {
        var options = new OutboxDispatcherOptions();
        
        options.HasOnlySingletonMiddlewares.Should().BeFalse();
        options.PollingInterval.Should().Be(TimeSpan.FromMilliseconds(500));
        options.UseAdaptivePolling.Should().BeTrue();
        options.BatchSize.Should().Be(100);
        options.MaxDegreeOfParallelism.Should().Be(Math.Min(Environment.ProcessorCount, 8));
        options.MaxBatchesPerSecond.Should().Be(0);
        options.ChannelCapacity.Should().Be(1000);
        options.MaxRetryCount.Should().Be(10);
        options.ReclaimTimeout.Should().Be(TimeSpan.FromMinutes(5));
        options.ReclaimInterval.Should().Be(TimeSpan.FromMinutes(1));
        options.DbRetryMaxAttempts.Should().Be(3);
        options.DbRetryBaseDelayMs.Should().Be(50);
        options.PendingCountRefreshInterval.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 4)]
    [InlineData(8, 8)]
    [InlineData(16, 8)]
    [InlineData(64, 8)]
    public void ComputeDefaultMaxDegreeOfParallelism_CalculatesCorrectly(int processorCount, int expected)
    {
        OutboxDispatcherOptions.ComputeDefaultMaxDegreeOfParallelism(processorCount).Should().Be(expected);
    }

    [Fact]
    public void OutboxDispatcherOptions_Properties_Work()
    {
        var options = new OutboxDispatcherOptions
        {
            HasOnlySingletonMiddlewares = true,
            PollingInterval = TimeSpan.FromSeconds(1),
            UseAdaptivePolling = false,
            BatchSize = 50,
            MaxDegreeOfParallelism = 4,
            MaxBatchesPerSecond = 10,
            ChannelCapacity = 500,
            MaxRetryCount = 5,
            ReclaimTimeout = TimeSpan.FromMinutes(10),
            ReclaimInterval = TimeSpan.FromMinutes(2),
            DbRetryMaxAttempts = 5,
            DbRetryBaseDelayMs = 100,
            PendingCountRefreshInterval = TimeSpan.FromSeconds(60)
        };
        
        options.HasOnlySingletonMiddlewares.Should().BeTrue();
        options.PollingInterval.Should().Be(TimeSpan.FromSeconds(1));
        options.UseAdaptivePolling.Should().BeFalse();
        options.BatchSize.Should().Be(50);
        options.MaxDegreeOfParallelism.Should().Be(4);
        options.MaxBatchesPerSecond.Should().Be(10);
        options.ChannelCapacity.Should().Be(500);
        options.MaxRetryCount.Should().Be(5);
        options.ReclaimTimeout.Should().Be(TimeSpan.FromMinutes(10));
        options.ReclaimInterval.Should().Be(TimeSpan.FromMinutes(2));
        options.DbRetryMaxAttempts.Should().Be(5);
        options.DbRetryBaseDelayMs.Should().Be(100);
        options.PendingCountRefreshInterval.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void OutboxRuntimeOptions_Defaults_AreCorrect()
    {
        var options = new OutboxRuntimeOptions();
        
        options.InstanceId.Should().NotBeNullOrWhiteSpace();
        options.InstanceId.Length.Should().Be(32);
        options.InstanceId.Should().NotContain("-");
        Guid.TryParseExact(options.InstanceId, "N", out _).Should().BeTrue();
        options.SchemaName.Should().Be("outbox");
        options.TableName.Should().Be("messages");
        options.MaxPayloadSizeInBytes.Should().Be(1024 * 1024);
        options.MaxHeaderSizeInBytes.Should().Be(64 * 1024);
        options.ThrowOnUnregisteredType.Should().BeTrue();
        options.MaxMessageAge.Should().Be(TimeSpan.FromDays(30));
        options.MaxBackoffSeconds.Should().Be(3600);
        options.LargeTableThreshold.Should().Be(50000);
        options.DeleteOnDispatch.Should().BeTrue();
        options.MaxStoreRatePerSecond.Should().Be(0);
        options.ReclaimBatchLimit.Should().Be(1000);
        options.IncludeMessageTypeTag.Should().BeTrue();
    }

    [Fact]
    public void OutboxRuntimeOptions_Properties_Work()
    {
        var options = new OutboxRuntimeOptions
        {
            InstanceId = "test-id",
            SchemaName = "test-schema",
            TableName = "test-table",
            MaxPayloadSizeInBytes = 100,
            MaxHeaderSizeInBytes = 50,
            ThrowOnUnregisteredType = false,
            MaxMessageAge = TimeSpan.FromDays(10),
            MaxBackoffSeconds = 600,
            LargeTableThreshold = 10000,
            DeleteOnDispatch = false,
            MaxStoreRatePerSecond = 1000,
            ReclaimBatchLimit = 5000,
            IncludeMessageTypeTag = false
        };
        
        options.InstanceId.Should().Be("test-id");
        options.SchemaName.Should().Be("test-schema");
        options.TableName.Should().Be("test-table");
        options.MaxPayloadSizeInBytes.Should().Be(100);
        options.MaxHeaderSizeInBytes.Should().Be(50);
        options.ThrowOnUnregisteredType.Should().BeFalse();
        options.MaxMessageAge.Should().Be(TimeSpan.FromDays(10));
        options.MaxBackoffSeconds.Should().Be(600);
        options.LargeTableThreshold.Should().Be(10000);
        options.DeleteOnDispatch.Should().BeFalse();
        options.MaxStoreRatePerSecond.Should().Be(1000);
        options.ReclaimBatchLimit.Should().Be(5000);
        options.IncludeMessageTypeTag.Should().BeFalse();
    }

    [Fact]
    public void OutboxInboxOptions_Defaults_AreCorrect()
    {
        var options = new OutboxInboxOptions();
        
        options.RetentionPeriod.Should().Be(TimeSpan.FromDays(7));
        options.DuplicateDetectionWindow.Should().Be(TimeSpan.FromHours(24));
        options.CleanupInterval.Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void OutboxInboxOptions_Properties_Work()
    {
        var options = new OutboxInboxOptions
        {
            RetentionPeriod = TimeSpan.FromDays(30),
            DuplicateDetectionWindow = TimeSpan.FromHours(12),
            CleanupInterval = TimeSpan.FromHours(2)
        };
        
        options.RetentionPeriod.Should().Be(TimeSpan.FromDays(30));
        options.DuplicateDetectionWindow.Should().Be(TimeSpan.FromHours(12));
        options.CleanupInterval.Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public void OutboxHealthCheckOptions_Defaults_AreCorrect()
    {
        var options = new OutboxHealthCheckOptions();
        options.WarningThreshold.Should().Be(1000);
    }

    [Fact]
    public void OutboxHealthCheckOptions_Properties_Work()
    {
        var options = new OutboxHealthCheckOptions
        {
            WarningThreshold = 500
        };
        
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
        interceptor!.CircuitBreaker.Should().NotBeNull();
        interceptor.CircuitBreaker.Should().BeOfType<CircuitBreakerState>();
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
        interceptor!.CircuitBreaker.Should().NotBeNull();
        interceptor.CircuitBreaker.Should().BeOfType<CircuitBreakerState>();
    }

    [Fact]
    public void UseBroker_InstanceWithCustomCircuitBreaker_PassesCustomInstance()
    {
        var services = new ServiceCollection();
        var options = new OutboxOptions(services);
        var publisher = Substitute.For<IBrokerPublisher>();
        var retryPolicy = new FixedDelayRetryPolicy(TimeSpan.FromSeconds(1), 1);
        var customCb = new CircuitBreakerState();
        
        options.UseBroker(publisher, retryPolicy, customCb);
        
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        var factoryResult = options.DefaultPublisherFactory!(sp);
        
        var interceptor = factoryResult as RetryDispatcherInterceptor;
        interceptor.Should().NotBeNull();
        interceptor!.CircuitBreaker.Should().BeSameAs(customCb);
    }

    [Fact]
    public void UseBroker_GenericWithRetryPolicyAndNullCircuitBreaker_CreatesDefaultCircuitBreaker()
    {
        var services = new ServiceCollection();
        var options = new OutboxOptions(services);
        var retryPolicy = new FixedDelayRetryPolicy(TimeSpan.FromSeconds(1), 1);
        
        options.UseBroker<OutboxOptionsConfigurationTests.TestBroker>(retryPolicy, null);
        
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        var factoryResult = options.DefaultPublisherFactory!(sp);
        
        var interceptor = factoryResult as RetryDispatcherInterceptor;
        interceptor.Should().NotBeNull();
    }

    [Fact]
    public void UseBroker_InstanceWithoutRetryPolicy_RegistersDirectly()
    {
        var services = new ServiceCollection();
        var options = new OutboxOptions(services);
        var publisher = Substitute.For<IBrokerPublisher>();
        options.UseBroker(publisher);
        
        var sp = services.BuildServiceProvider();
        var factoryResult = options.DefaultPublisherFactory!(sp);
        
        factoryResult.Should().BeSameAs(publisher);
    }

    [Fact]
    public void UseBroker_GenericWithoutRetryPolicy_RegistersDirectly()
    {
        var services = new ServiceCollection();
        var options = new OutboxOptions(services);
        var publisher = Substitute.For<IBrokerPublisher>();
        services.AddSingleton(publisher);
        
        options.UseBroker<IBrokerPublisher>();
        
        var sp = services.BuildServiceProvider();
        var factoryResult = options.DefaultPublisherFactory!(sp);
        
        factoryResult.Should().BeSameAs(publisher);
    }
}





