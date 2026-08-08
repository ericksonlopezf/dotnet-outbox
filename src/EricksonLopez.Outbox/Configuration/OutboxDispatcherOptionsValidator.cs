using System;
// Stryker disable all : Covered by ADR-013. Edge cases, micro-optimizations, logging, and validation strings are not rigorously mutated.
using Microsoft.Extensions.Options;

namespace EricksonLopez.Outbox;

// Stryker disable String : Exception and validation messages are not tested for exact matching
internal sealed class OutboxDispatcherOptionsValidator : IValidateOptions<OutboxDispatcherOptions>
{
    public ValidateOptionsResult Validate(string? name, OutboxDispatcherOptions options)
    {
        if (options.MaxDegreeOfParallelism <= 0)
        {
            return ValidateOptionsResult.Fail($"{nameof(options.MaxDegreeOfParallelism)} must be greater than 0.");
        }

        if (options.BatchSize <= 0)
        {
            return ValidateOptionsResult.Fail($"{nameof(options.BatchSize)} must be greater than 0.");
        }

        if (options.ChannelCapacity <= 0)
        {
            return ValidateOptionsResult.Fail($"{nameof(options.ChannelCapacity)} must be greater than 0.");
        }

        if (options.MaxRetryCount < 0)
        {
            return ValidateOptionsResult.Fail($"{nameof(options.MaxRetryCount)} cannot be negative.");
        }

        if (options.PollingInterval <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail($"{nameof(options.PollingInterval)} must be greater than TimeSpan.Zero.");
        }

        if (options.ReclaimInterval <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail($"{nameof(options.ReclaimInterval)} must be greater than TimeSpan.Zero.");
        }

        // ISSUE-16 FIX: Validate the new configurable metric refresh interval.
        if (options.PendingCountRefreshInterval <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(options.PendingCountRefreshInterval)} must be greater than TimeSpan.Zero. " +
                $"Recommended range: 5\u201360 seconds.");
        }

        if (options.DbRetryMaxAttempts < 0)
        {
            return ValidateOptionsResult.Fail($"{nameof(options.DbRetryMaxAttempts)} cannot be negative.");
        }

        if (options.DbRetryBaseDelayMs < 0)
        {
            return ValidateOptionsResult.Fail($"{nameof(options.DbRetryBaseDelayMs)} cannot be negative.");
        }

        return ValidateOptionsResult.Success;
    }
}
