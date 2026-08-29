// Copyright © Erickson Lopez. MIT License.
using System;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace EricksonLopez.Outbox;

internal sealed class OutboxRuntimeOptionsValidator : IValidateOptions<OutboxRuntimeOptions>
{
    private static readonly Regex ValidIdentifierRegex = new("^[a-zA-Z0-9_-]+$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    public ValidateOptionsResult Validate(string? name, OutboxRuntimeOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SchemaName) || !ValidIdentifierRegex.IsMatch(options.SchemaName))
        {
            return ValidateOptionsResult.Fail($"{nameof(options.SchemaName)} '{options.SchemaName}' is invalid. Only alphanumeric characters, underscores, and hyphens are allowed.");
        }

        if (string.IsNullOrWhiteSpace(options.TableName) || !ValidIdentifierRegex.IsMatch(options.TableName))
        {
            return ValidateOptionsResult.Fail($"{nameof(options.TableName)} '{options.TableName}' is invalid. Only alphanumeric characters, underscores, and hyphens are allowed.");
        }

        return ValidateOptionsResult.Success;
    }
}

