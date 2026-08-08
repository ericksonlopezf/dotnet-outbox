using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace EricksonLopez.Outbox;

// Stryker disable String : Validation messages are not tested for exact matching
internal sealed class OutboxRuntimeOptionsValidator : IValidateOptions<OutboxRuntimeOptions>
{
    private static readonly Regex ValidIdentifierRegex = new("^[a-zA-Z0-9_-]+$", RegexOptions.Compiled);

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
