using System;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Infrastructure;
/// <summary>
/// A custom xUnit Fact that automatically skips the test if a required environment variable is not set.
/// </summary>
public sealed class RequiresEnvironmentVariableFactAttribute : FactAttribute
{
    public RequiresEnvironmentVariableFactAttribute(string environmentVariable)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(value))
        {
            Skip = $"Test skipped because environment variable '{environmentVariable}' is not set.";
        }
    }
}


