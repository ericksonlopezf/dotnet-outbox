// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using EricksonLopez.Idempotency;
using EricksonLopez.Inbox;
using Xunit;

namespace EricksonLopez.Inbox.Tests;

public sealed class IdempotencyKeyTests
{
    [Fact]
    public void Empty_ReturnsKeyWithEmptyValue()
    {
        var key = IdempotencyKey.Empty;

        key.IsEmpty.Should().BeTrue();
        key.Value.Should().Be(string.Empty);
        key.ToString().Should().Be(string.Empty);

        var defaultKey = default(IdempotencyKey);
        defaultKey.IsEmpty.Should().BeTrue();
        defaultKey.Value.Should().Be(string.Empty);
        defaultKey.ToString().Should().Be(string.Empty);
    }

    [Fact]
    public void Create_WithString_ReturnsNormalizedKey()
    {
        var key = IdempotencyKey.Create("  order-123  ");

        key.IsEmpty.Should().BeFalse();
        key.Value.Should().Be("order-123");
        key.ToString().Should().Be("order-123");
        ((string)key).Should().Be("order-123");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithInvalidString_ThrowsArgumentException(string? invalid)
    {
        var act = () => IdempotencyKey.Create(invalid!);
        act.Should().Throw<ArgumentException>()
            .WithParameterName("value");
    }

    [Fact]
    public void Create_WithGuid_ReturnsFormattedKeyWithoutHyphens()
    {
        var guid = Guid.NewGuid();
        var key = IdempotencyKey.Create(guid);

        key.Value.Should().Be(guid.ToString("N"));
        key.Value.Should().NotContain("-");
        key.Value.Length.Should().Be(32);
    }

    [Fact]
    public void Create_WithEmptyGuid_ThrowsArgumentException()
    {
        var act = () => IdempotencyKey.Create(Guid.Empty);
        act.Should().Throw<ArgumentException>()
            .WithParameterName("identifier")
            .WithMessage("*Idempotency key cannot be created from Guid.Empty*");
    }

    [Fact]
    public void NewKey_ReturnsNonEmptyRandomKeyWithoutHyphens()
    {
        var key1 = IdempotencyKey.NewKey();
        var key2 = IdempotencyKey.NewKey();

        key1.IsEmpty.Should().BeFalse();
        key2.IsEmpty.Should().BeFalse();
        key1.Value.Should().NotContain("-");
        key1.Value.Length.Should().Be(32);
        key1.Should().NotBe(key2);
    }

    [Fact]
    public void TryParse_ValidKey_ReturnsTrueAndSetsTrimmedKey()
    {
        var result = IdempotencyKey.TryParse(" invoice-999 ", out var key);

        result.Should().BeTrue();
        key.Value.Should().Be("invoice-999");
        key.IsEmpty.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_InvalidKey_ReturnsFalseAndEmptyKey(string? candidate)
    {
        var result = IdempotencyKey.TryParse(candidate, out var key);

        result.Should().BeFalse();
        key.IsEmpty.Should().BeTrue();
        key.Value.Should().BeEmpty();
    }

    [Fact]
    public void EqualityAndComparison_OperateCorrectly_WithOrdinalSensitivity()
    {
        var key1 = IdempotencyKey.Create("A");
        var key2 = IdempotencyKey.Create("A");
        var key3 = IdempotencyKey.Create("B");
        var keyLower = IdempotencyKey.Create("a");

        // Equality operators
        (key1 == key2).Should().BeTrue();
        (key1 != key2).Should().BeFalse();
        (key1 == key3).Should().BeFalse();
        (key1 != key3).Should().BeTrue();
        (key1 == keyLower).Should().BeFalse(); // Ordinal check
        (key1 != keyLower).Should().BeTrue();

        key1.Equals(key2).Should().BeTrue();
        key1.Equals((object)key2).Should().BeTrue();
        key1.Equals(key3).Should().BeFalse();
        key1.Equals((object)key3).Should().BeFalse();
        key1.Equals(keyLower).Should().BeFalse();
        key1.Equals((object)keyLower).Should().BeFalse();
        key1.Equals((object?)null).Should().BeFalse();
        key1.Equals("not-an-idempotency-key").Should().BeFalse();

        key1.GetHashCode().Should().Be(key2.GetHashCode());
        key1.GetHashCode().Should().NotBe(key3.GetHashCode());
        key1.GetHashCode().Should().NotBe(keyLower.GetHashCode());

        // Less than / Greater than operators
        (key1 < key3).Should().BeTrue();
        (key3 < key1).Should().BeFalse();
        (key1 < key2).Should().BeFalse(); // Critical for < vs <=

        (key1 <= key3).Should().BeTrue();
        (key1 <= key2).Should().BeTrue();  // Critical for <= vs <
        (key3 <= key1).Should().BeFalse();

        (key3 > key1).Should().BeTrue();
        (key1 > key3).Should().BeFalse();
        (key1 > key2).Should().BeFalse(); // Critical for > vs >=

        (key3 >= key1).Should().BeTrue();
        (key1 >= key2).Should().BeTrue();  // Critical for >= vs >
        (key1 >= key3).Should().BeFalse();

        // CompareTo
        key1.CompareTo(key3).Should().BeNegative();
        key3.CompareTo(key1).Should().BePositive();
        key1.CompareTo(key2).Should().Be(0);
        key1.CompareTo(keyLower).Should().NotBe(0); // Ordinal
        key1.CompareTo((object?)null).Should().Be(1);
        key1.CompareTo((object)key3).Should().BeNegative();
        key1.CompareTo((object)key2).Should().Be(0);

        var act = () => key1.CompareTo("invalid-type-object");
        act.Should().Throw<ArgumentException>()
            .WithParameterName("obj")
            .WithMessage("*Object must be of type IdempotencyKey*");

        var explicitKey = (IdempotencyKey)"my-custom-key";
        explicitKey.Value.Should().Be("my-custom-key");
        ((string)explicitKey).Should().Be("my-custom-key");
    }
}
