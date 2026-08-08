using System;
using AwesomeAssertions;
using EricksonLopez.Outbox.Diagnostics;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Diagnostics;

public class DefaultErrorSanitizerTests
{
    [Fact]
    public void Sanitize_ReturnsExceptionMessage()
    {
        var sanitizer = new DefaultErrorSanitizer();
        var ex = new InvalidOperationException("Test error message");

        var result = sanitizer.Sanitize(ex);

        result.Should().Be("Test error message");
    }
}
