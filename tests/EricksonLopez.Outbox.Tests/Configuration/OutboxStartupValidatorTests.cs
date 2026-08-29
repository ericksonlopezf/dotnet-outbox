// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox.Dispatcher;
using EricksonLopez.Outbox.Hosting;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

#pragma warning disable SYSLIB0050
namespace EricksonLopez.Outbox.Tests.Configuration;

public class OutboxStartupValidatorTests
{
    private sealed class TrackingValidatorLogger : ILogger<OutboxStartupValidator>
    {
        public int FailedCount;
        public int ProducerOnlyCount;
        public int PassedCount;
        public int ThirdPartyDlqCount;
        public string? LastThirdPartyType;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (eventId.Id == 10100 || eventId.Name == "StartupValidationFailed")
                Interlocked.Increment(ref FailedCount);
            else if (eventId.Id == 10102 || eventId.Name == "ProducerOnlyMode")
                Interlocked.Increment(ref ProducerOnlyCount);
            else if (eventId.Id == 10101 || eventId.Name == "StartupValidationPassed")
                Interlocked.Increment(ref PassedCount);
            else if (eventId.Id == 10112 || eventId.Name == "ThirdPartyDeadLetterRepositoryRegistered")
            {
                Interlocked.Increment(ref ThirdPartyDlqCount);
                LastThirdPartyType = state?.ToString();
            }
        }
    }

    [Fact]
    public async Task StartAsync_WhenAllCriticalDependenciesMissing_ThrowsInvalidOperationExceptionWithAllErrors()
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IOutboxSerializer)).Returns(null);
        serviceProvider.GetService(typeof(IOutboxMessageTypeResolver)).Returns(null);
        serviceProvider.GetService(typeof(IOutboxRepository)).Returns(null);

        var logger = new TrackingValidatorLogger();
        var validator = new OutboxStartupValidator(serviceProvider, logger);

        var act = async () => await validator.StartAsync(CancellationToken.None);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("3 critical service(s) are not registered");
        ex.Which.Message.Should().Contain("IOutboxSerializer is not registered. Call options.UseSerializer(...) or options.UseGeneratedTypes(MyOutboxJsonContext.Default) inside AddOutbox(options => { ... }).");
        ex.Which.Message.Should().Contain("IOutboxMessageTypeResolver is not registered. Call options.UseTypeResolver(...) or options.UseGeneratedTypes() inside AddOutbox(options => { ... }).");
        ex.Which.Message.Should().Contain("IOutboxRepository is not registered. Add a storage provider, e.g., services.AddOutboxPostgreSql(...) or services.AddOutboxSqlServer(...).");
        logger.FailedCount.Should().Be(1);
    }

    [Fact]
    public async Task StartAsync_WhenSerializerMissing_ThrowsInvalidOperationException()
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IOutboxSerializer)).Returns(null);
        serviceProvider.GetService(typeof(IOutboxMessageTypeResolver)).Returns(Substitute.For<IOutboxMessageTypeResolver>());
        serviceProvider.GetService(typeof(IOutboxRepository)).Returns(Substitute.For<IOutboxRepository>());

        var logger = new TrackingValidatorLogger();
        var validator = new OutboxStartupValidator(serviceProvider, logger);

        var act = async () => await validator.StartAsync(CancellationToken.None);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("1 critical service(s) are not registered");
        ex.Which.Message.Should().Contain("IOutboxSerializer is not registered");
        logger.FailedCount.Should().Be(1);
    }

    [Fact]
    public async Task StartAsync_WhenTypeResolverMissing_ThrowsInvalidOperationException()
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IOutboxSerializer)).Returns(Substitute.For<IOutboxSerializer>());
        serviceProvider.GetService(typeof(IOutboxMessageTypeResolver)).Returns(null);
        serviceProvider.GetService(typeof(IOutboxRepository)).Returns(Substitute.For<IOutboxRepository>());

        var logger = new TrackingValidatorLogger();
        var validator = new OutboxStartupValidator(serviceProvider, logger);

        var act = async () => await validator.StartAsync(CancellationToken.None);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("1 critical service(s) are not registered");
        ex.Which.Message.Should().Contain("IOutboxMessageTypeResolver is not registered");
        logger.FailedCount.Should().Be(1);
    }

    [Fact]
    public async Task StartAsync_WhenRepositoryMissing_ThrowsInvalidOperationException()
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IOutboxSerializer)).Returns(Substitute.For<IOutboxSerializer>());
        serviceProvider.GetService(typeof(IOutboxMessageTypeResolver)).Returns(Substitute.For<IOutboxMessageTypeResolver>());
        serviceProvider.GetService(typeof(IOutboxRepository)).Returns(null);

        var logger = new TrackingValidatorLogger();
        var validator = new OutboxStartupValidator(serviceProvider, logger);

        var act = async () => await validator.StartAsync(CancellationToken.None);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("1 critical service(s) are not registered");
        ex.Which.Message.Should().Contain("IOutboxRepository is not registered");
        logger.FailedCount.Should().Be(1);
    }

    [Fact]
    public async Task StartAsync_WhenAllCriticalDependenciesPresent_ProducerOnly_LogsProducerOnlyMode()
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IOutboxSerializer)).Returns(Substitute.For<IOutboxSerializer>());
        serviceProvider.GetService(typeof(IOutboxMessageTypeResolver)).Returns(Substitute.For<IOutboxMessageTypeResolver>());
        serviceProvider.GetService(typeof(IOutboxRepository)).Returns(Substitute.For<IOutboxRepository>());
        serviceProvider.GetService(typeof(IEnumerable<IHostedService>)).Returns(Array.Empty<IHostedService>());
        serviceProvider.GetService(typeof(IDeadLetterRepository)).Returns(null);

        var logger = new TrackingValidatorLogger();
        var validator = new OutboxStartupValidator(serviceProvider, logger);

        await validator.StartAsync(CancellationToken.None);

        logger.ProducerOnlyCount.Should().Be(1);
        logger.PassedCount.Should().Be(0);
    }

    [Fact]
    public async Task StartAsync_WhenAllCriticalDependenciesPresent_WithDispatcher_LogsStartupValidationPassed()
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IOutboxSerializer)).Returns(Substitute.For<IOutboxSerializer>());
        serviceProvider.GetService(typeof(IOutboxMessageTypeResolver)).Returns(Substitute.For<IOutboxMessageTypeResolver>());
        serviceProvider.GetService(typeof(IOutboxRepository)).Returns(Substitute.For<IOutboxRepository>());

        var nonDispatcher = Substitute.For<IHostedService>();
        var dispatcher = (OutboxDispatcherBackgroundService)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(OutboxDispatcherBackgroundService));

        serviceProvider.GetService(typeof(IEnumerable<IHostedService>)).Returns(new IHostedService[] { nonDispatcher, dispatcher });
        serviceProvider.GetService(typeof(IDeadLetterRepository)).Returns(null);

        var logger = new TrackingValidatorLogger();
        var validator = new OutboxStartupValidator(serviceProvider, logger);

        await validator.StartAsync(CancellationToken.None);

        logger.PassedCount.Should().Be(1);
        logger.ProducerOnlyCount.Should().Be(0);
    }

    [Fact]
    public async Task StartAsync_WhenDispatcherFound_BreaksEarly()
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IOutboxSerializer)).Returns(Substitute.For<IOutboxSerializer>());
        serviceProvider.GetService(typeof(IOutboxMessageTypeResolver)).Returns(Substitute.For<IOutboxMessageTypeResolver>());
        serviceProvider.GetService(typeof(IOutboxRepository)).Returns(Substitute.For<IOutboxRepository>());

        var dispatcher = (OutboxDispatcherBackgroundService)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(OutboxDispatcherBackgroundService));

        static IEnumerable<IHostedService> GenerateServices(OutboxDispatcherBackgroundService d)
        {
            yield return d;
            throw new InvalidOperationException("Should have broken out of loop immediately after finding dispatcher");
        }

        serviceProvider.GetService(typeof(IEnumerable<IHostedService>)).Returns(GenerateServices(dispatcher));
        serviceProvider.GetService(typeof(IDeadLetterRepository)).Returns(null);

        var logger = new TrackingValidatorLogger();
        var validator = new OutboxStartupValidator(serviceProvider, logger);

        await validator.StartAsync(CancellationToken.None);

        logger.PassedCount.Should().Be(1);
    }

    [Fact]
    public async Task StartAsync_WhenOnlyNonDispatcherHostedServicePresent_ProducerOnly_LogsProducerOnlyMode()
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IOutboxSerializer)).Returns(Substitute.For<IOutboxSerializer>());
        serviceProvider.GetService(typeof(IOutboxMessageTypeResolver)).Returns(Substitute.For<IOutboxMessageTypeResolver>());
        serviceProvider.GetService(typeof(IOutboxRepository)).Returns(Substitute.For<IOutboxRepository>());

        var nonDispatcher = Substitute.For<IHostedService>();
        serviceProvider.GetService(typeof(IEnumerable<IHostedService>)).Returns(new IHostedService[] { nonDispatcher });
        serviceProvider.GetService(typeof(IDeadLetterRepository)).Returns(null);

        var logger = new TrackingValidatorLogger();
        var validator = new OutboxStartupValidator(serviceProvider, logger);

        await validator.StartAsync(CancellationToken.None);

        logger.ProducerOnlyCount.Should().Be(1);
        logger.PassedCount.Should().Be(0);
    }

    [Fact]
    public async Task StartAsync_WhenThirdPartyDeadLetterRepository_LogsThirdPartyWarning()
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IOutboxSerializer)).Returns(Substitute.For<IOutboxSerializer>());
        serviceProvider.GetService(typeof(IOutboxMessageTypeResolver)).Returns(Substitute.For<IOutboxMessageTypeResolver>());
        serviceProvider.GetService(typeof(IOutboxRepository)).Returns(Substitute.For<IOutboxRepository>());
        serviceProvider.GetService(typeof(IEnumerable<IHostedService>)).Returns(Array.Empty<IHostedService>());

        var thirdPartyDlq = Substitute.For<IDeadLetterRepository>();
        thirdPartyDlq.IsFirstPartyImplementation.Returns(false);
        serviceProvider.GetService(typeof(IDeadLetterRepository)).Returns(thirdPartyDlq);

        var logger = new TrackingValidatorLogger();
        var validator = new OutboxStartupValidator(serviceProvider, logger);

        await validator.StartAsync(CancellationToken.None);

        logger.ThirdPartyDlqCount.Should().Be(1);
    }

    [Fact]
    public async Task StartAsync_WhenFirstPartyDeadLetterRepository_DoesNotLogThirdPartyWarning()
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IOutboxSerializer)).Returns(Substitute.For<IOutboxSerializer>());
        serviceProvider.GetService(typeof(IOutboxMessageTypeResolver)).Returns(Substitute.For<IOutboxMessageTypeResolver>());
        serviceProvider.GetService(typeof(IOutboxRepository)).Returns(Substitute.For<IOutboxRepository>());
        serviceProvider.GetService(typeof(IEnumerable<IHostedService>)).Returns(Array.Empty<IHostedService>());

        var firstPartyDlq = Substitute.For<IDeadLetterRepository>();
        firstPartyDlq.IsFirstPartyImplementation.Returns(true);
        serviceProvider.GetService(typeof(IDeadLetterRepository)).Returns(firstPartyDlq);

        var logger = new TrackingValidatorLogger();
        var validator = new OutboxStartupValidator(serviceProvider, logger);

        await validator.StartAsync(CancellationToken.None);

        logger.ThirdPartyDlqCount.Should().Be(0);
    }

    [Fact]
    public async Task StopAsync_ReturnsCompletedTask()
    {
        var validator = new OutboxStartupValidator(Substitute.For<IServiceProvider>(), NullLogger<OutboxStartupValidator>.Instance);
        await validator.StopAsync(CancellationToken.None);
    }
}



