using EricksonLopez.Outbox.Hosting;
using System;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Serialization;

namespace EricksonLopez.Outbox.Tests.Configuration;

public class OutboxOptionsTests
{
    [Fact]
    public void Default_Properties_Are_Set_Correctly()
    {
        var services = new ServiceCollection();
        services.AddOutbox(_ => { });
        services.Should().NotBeEmpty();
    }

    [Fact]
    public void Constructor_Throws_When_Services_Null()
    {
        Action act = () => ((IServiceCollection)null!).AddOutbox(_ => { });
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void UseSerializer_Instance_Adds_To_Services()
    {
        var services = new ServiceCollection();
        var serializer = Substitute.For<IOutboxSerializer>();
        services.AddOutbox(options => options.UseSerializer(serializer));
        var provider = services.BuildServiceProvider();
        provider.GetService<IOutboxSerializer>().Should().BeSameAs(serializer);
    }

    [Fact]
    public void UseSerializer_Instance_Throws_When_Null()
    {
        var services = new ServiceCollection();
        Action act = () => services.AddOutbox(options => options.UseSerializer((IOutboxSerializer)null!));
        act.Should().Throw<ArgumentNullException>().WithParameterName("serializer");
    }

    [Fact]
    public void UseSerializer_Generic_Adds_To_Services()
    {
        var services = new ServiceCollection();
        services.AddOutbox(options => options.UseSerializer<FakeSerializer>());
        var provider = services.BuildServiceProvider();
        provider.GetService<IOutboxSerializer>().Should().BeOfType<FakeSerializer>();
    }

    [Fact]
    public void UseTypeResolver_Adds_To_Services()
    {
        var services = new ServiceCollection();
        var resolver = Substitute.For<IOutboxMessageTypeResolver>();
        services.AddOutbox(options => options.UseTypeResolver(resolver));
        var provider = services.BuildServiceProvider();
        provider.GetService<IOutboxMessageTypeResolver>().Should().BeSameAs(resolver);
    }

    [Fact]
    public void UseTypeResolver_Throws_When_Null()
    {
        var services = new ServiceCollection();
        Action act = () => services.AddOutbox(options => options.UseTypeResolver(null!));
        act.Should().Throw<ArgumentNullException>().WithParameterName("typeResolver");
    }

    [Fact]
    public void UseBroker_Generic_Adds_To_Services()
    {
        var services = new ServiceCollection();
        services.AddOutbox(options => options.UseBroker<FakeBroker>());
        var provider = services.BuildServiceProvider();
        provider.GetService<IBrokerPublisher>().Should().BeOfType<FakeBroker>();
    }

    [Fact]
    public void UseBroker_Factory_Adds_To_Services()
    {
        var services = new ServiceCollection();
        var instance = new FakeBroker();
        services.AddOutbox(options => options.UseBroker(_ => instance));
        var provider = services.BuildServiceProvider();
        provider.GetService<IBrokerPublisher>().Should().BeSameAs(instance);
    }

    [Fact]
    public void UseBroker_Factory_Throws_When_Null()
    {
        var services = new ServiceCollection();
        Action act = () => services.AddOutbox(options => options.UseBroker((Func<IServiceProvider, IBrokerPublisher>)null!));
        act.Should().Throw<ArgumentNullException>().WithParameterName("factory");
    }

    [Fact]
    public void UseBroker_Instance_Adds_To_Services()
    {
        var services = new ServiceCollection();
        var publisher = new FakeBroker();
        services.AddOutbox(options => options.UseBroker(publisher));
        var provider = services.BuildServiceProvider();
        provider.GetService<IBrokerPublisher>().Should().BeSameAs(publisher);
    }

    [Fact]
    public void UseBroker_Instance_Throws_When_Null()
    {
        var services = new ServiceCollection();
        Action act = () => services.AddOutbox(options => options.UseBroker((IBrokerPublisher)null!));
        act.Should().Throw<ArgumentNullException>().WithParameterName("publisher");
    }

    private sealed class FakeSerializer : IOutboxSerializer
    {
        public ReadOnlyMemory<byte> Serialize<T>(T value) => throw new NotImplementedException();
        public T Deserialize<T>(ReadOnlySpan<byte> bytes) => throw new NotImplementedException();
    }

    private sealed class FakeBroker : IBrokerPublisher
    {
        public System.Threading.Tasks.ValueTask<DispatchResult> PublishAsync<T>(MessageEnvelope<T> message, DispatchContext context) where T : notnull => throw new NotImplementedException();
        public System.Threading.Tasks.ValueTask<System.Collections.Generic.IReadOnlyList<DispatchResult>> PublishBatchAsync<T>(System.Collections.Generic.IReadOnlyList<MessageEnvelope<T>> messages, DispatchContext context) where T : notnull => throw new NotImplementedException();
        public System.Threading.Tasks.ValueTask<DispatchResult> PublishRawAsync(OutboxMessage message, MessageMetadata metadata, DispatchContext context) => throw new NotImplementedException();
    }
}

public class OutboxDispatcherOptionsTests
{
    [Fact]
    public void Default_Properties_Are_Set_Correctly()
    {
        var options = new OutboxDispatcherOptions();
        options.BatchSize.Should().Be(100);
        options.PollingInterval.Should().Be(TimeSpan.FromMilliseconds(500));
        options.UseAdaptivePolling.Should().BeTrue();
        options.ChannelCapacity.Should().Be(1000);
        options.MaxDegreeOfParallelism.Should().Be(Environment.ProcessorCount);
    }

    [Fact]
    public void Can_Set_Properties()
    {
        var options = new OutboxDispatcherOptions
        {
            BatchSize = 100,
            PollingInterval = TimeSpan.FromSeconds(5),
            UseAdaptivePolling = false,
            ChannelCapacity = 500,
            MaxDegreeOfParallelism = 2
        };
        options.BatchSize.Should().Be(100);
        options.PollingInterval.Should().Be(TimeSpan.FromSeconds(5));
        options.UseAdaptivePolling.Should().BeFalse();
        options.ChannelCapacity.Should().Be(500);
        options.MaxDegreeOfParallelism.Should().Be(2);
    }
}

public class OutboxInboxOptionsTests
{
    [Fact]
    public void Default_Properties_Are_Set_Correctly()
    {
        var options = new OutboxInboxOptions();
        options.DuplicateDetectionWindow.Should().Be(TimeSpan.FromHours(24));
        options.RetentionPeriod.Should().Be(TimeSpan.FromDays(7));
        options.CleanupInterval.Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void Can_Set_Properties()
    {
        var options = new OutboxInboxOptions
        {
            DuplicateDetectionWindow = TimeSpan.FromHours(1),
            RetentionPeriod = TimeSpan.FromHours(2),
            CleanupInterval = TimeSpan.FromMinutes(30)
        };
        options.DuplicateDetectionWindow.Should().Be(TimeSpan.FromHours(1));
        options.RetentionPeriod.Should().Be(TimeSpan.FromHours(2));
        options.CleanupInterval.Should().Be(TimeSpan.FromMinutes(30));
    }
}


