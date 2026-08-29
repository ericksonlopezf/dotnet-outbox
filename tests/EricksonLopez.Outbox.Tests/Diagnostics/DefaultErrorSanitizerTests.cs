// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using AwesomeAssertions;
using EricksonLopez.Outbox.Diagnostics;
using EricksonLopez.Result;
using FsCheck.Xunit;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Diagnostics;

public class DefaultErrorSanitizerTests
{
    [Fact]
    public void Sanitize_ValidException_ReturnsExceptionMessage()
    {
        var sanitizer = new DefaultErrorSanitizer();
        var ex = new InvalidOperationException("Database connection timeout occurred");

        var result = sanitizer.Sanitize(ex);

        result.Should().Be("Database connection timeout occurred");
    }

    [Fact]
    public void Sanitize_NullException_ThrowsArgumentNullException()
    {
        var sanitizer = new DefaultErrorSanitizer();

        var act = () => sanitizer.Sanitize(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("exception");
    }

    [Property]
    public bool Sanitize_AnyExceptionMessage_PreservesMessageExactly(string rawMessage)
    {
        var message = rawMessage ?? string.Empty;
        var sanitizer = new DefaultErrorSanitizer();
        var ex = new InvalidOperationException(message);

        var sanitized = sanitizer.Sanitize(ex);

        return sanitized == ex.Message;
    }
}



