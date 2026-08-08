using System;
using AwesomeAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Configuration;

public class OutboxDispatcherOptionsValidatorTests
{
    private readonly OutboxDispatcherOptionsValidator _sut = new();

    [Fact]
    public void Validate_ValidOptions_ReturnsSuccess()
    {
        var options = new OutboxDispatcherOptions
        {
            MaxDegreeOfParallelism = 1,
            BatchSize = 1,
            ChannelCapacity = 1,
            MaxRetryCount = 0,
            PollingInterval = TimeSpan.FromSeconds(1),
            ReclaimInterval = TimeSpan.FromSeconds(1),
            DbRetryMaxAttempts = 0,
            DbRetryBaseDelayMs = 0
        };

        var result = _sut.Validate(null, options);

        result.Should().Be(ValidateOptionsResult.Success);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_InvalidMaxDegreeOfParallelism_ReturnsFail(int invalidValue)
    {
        var options = new OutboxDispatcherOptions { MaxDegreeOfParallelism = invalidValue };
        var result = _sut.Validate(null, options);
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(options.MaxDegreeOfParallelism));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_InvalidBatchSize_ReturnsFail(int invalidValue)
    {
        var options = new OutboxDispatcherOptions { MaxDegreeOfParallelism = 1, BatchSize = invalidValue };
        var result = _sut.Validate(null, options);
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(options.BatchSize));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_InvalidChannelCapacity_ReturnsFail(int invalidValue)
    {
        var options = new OutboxDispatcherOptions { MaxDegreeOfParallelism = 1, BatchSize = 1, ChannelCapacity = invalidValue };
        var result = _sut.Validate(null, options);
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(options.ChannelCapacity));
    }

    [Fact]
    public void Validate_InvalidMaxRetryCount_ReturnsFail()
    {
        var options = new OutboxDispatcherOptions { MaxDegreeOfParallelism = 1, BatchSize = 1, ChannelCapacity = 1, MaxRetryCount = -1 };
        var result = _sut.Validate(null, options);
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(options.MaxRetryCount));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_InvalidPollingInterval_ReturnsFail(int seconds)
    {
        var options = new OutboxDispatcherOptions
        {
            MaxDegreeOfParallelism = 1,
            BatchSize = 1,
            ChannelCapacity = 1,
            MaxRetryCount = 0,
            PollingInterval = TimeSpan.FromSeconds(seconds)
        };
        var result = _sut.Validate(null, options);
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(options.PollingInterval));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_InvalidReclaimInterval_ReturnsFail(int seconds)
    {
        var options = new OutboxDispatcherOptions
        {
            MaxDegreeOfParallelism = 1,
            BatchSize = 1,
            ChannelCapacity = 1,
            MaxRetryCount = 0,
            PollingInterval = TimeSpan.FromSeconds(1),
            ReclaimInterval = TimeSpan.FromSeconds(seconds)
        };
        var result = _sut.Validate(null, options);
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(options.ReclaimInterval));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_InvalidPendingCountRefreshInterval_ReturnsFail(int seconds)
    {
        var options = new OutboxDispatcherOptions
        {
            MaxDegreeOfParallelism = 1,
            BatchSize = 1,
            ChannelCapacity = 1,
            MaxRetryCount = 0,
            PollingInterval = TimeSpan.FromSeconds(1),
            ReclaimInterval = TimeSpan.FromSeconds(1),
            PendingCountRefreshInterval = TimeSpan.FromSeconds(seconds)
        };
        var result = _sut.Validate(null, options);
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(options.PendingCountRefreshInterval));
    }

    [Fact]
    public void Validate_InvalidDbRetryMaxAttempts_ReturnsFail()
    {
        var options = new OutboxDispatcherOptions
        {
            MaxDegreeOfParallelism = 1,
            BatchSize = 1,
            ChannelCapacity = 1,
            MaxRetryCount = 0,
            PollingInterval = TimeSpan.FromSeconds(1),
            ReclaimInterval = TimeSpan.FromSeconds(1),
            PendingCountRefreshInterval = TimeSpan.FromSeconds(1),
            DbRetryMaxAttempts = -1
        };
        var result = _sut.Validate(null, options);
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(options.DbRetryMaxAttempts));
    }

    [Fact]
    public void Validate_InvalidDbRetryBaseDelayMs_ReturnsFail()
    {
        var options = new OutboxDispatcherOptions
        {
            MaxDegreeOfParallelism = 1,
            BatchSize = 1,
            ChannelCapacity = 1,
            MaxRetryCount = 0,
            PollingInterval = TimeSpan.FromSeconds(1),
            ReclaimInterval = TimeSpan.FromSeconds(1),
            PendingCountRefreshInterval = TimeSpan.FromSeconds(1),
            DbRetryBaseDelayMs = -1
        };
        var result = _sut.Validate(null, options);
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(options.DbRetryBaseDelayMs));
    }
}
