// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using EricksonLopez.Outbox.Storage.PostgreSql;
using Xunit;

namespace EricksonLopez.Outbox.Storage.PostgreSql.Tests;

public class PostgreSqlMemoryExtensionsTests
{
    [Fact]
    public void ToByteArray_FullArray_ReusesUnderlyingArrayInstance()
    {
        var original = new byte[] { 1, 2, 3, 4, 5 };
        var memory = new ReadOnlyMemory<byte>(original);

        var result = memory.ToByteArray();

        object.ReferenceEquals(result, original).Should().BeTrue();
        result.Should().Equal(original);
    }

    [Fact]
    public void ToByteArray_SliceWithOffsetZeroAndCountLessThanLength_AllocatesNewArray()
    {
        var original = new byte[] { 1, 2, 3, 4, 5 };
        var memory = new ReadOnlyMemory<byte>(original, 0, 3); // Offset = 0, Count = 3 < Length 5

        var result = memory.ToByteArray();

        object.ReferenceEquals(result, original).Should().BeFalse();
        result.Should().Equal(new byte[] { 1, 2, 3 });
    }

    [Fact]
    public void ToByteArray_SliceWithOffsetGreaterThanZeroAndCountLessThanLength_AllocatesNewArray()
    {
        var original = new byte[] { 1, 2, 3, 4, 5 };
        var memory = new ReadOnlyMemory<byte>(original, 1, 2); // Offset = 1, Count = 2 < Length 5

        var result = memory.ToByteArray();

        object.ReferenceEquals(result, original).Should().BeFalse();
        result.Should().Equal(new byte[] { 2, 3 });
    }

    [Fact]
    public void ToByteArray_SliceWithOffsetGreaterThanZeroAndCountEqualsLengthMinusOffset_AllocatesNewArray()
    {
        var original = new byte[] { 1, 2, 3, 4, 5 };
        var memory = new ReadOnlyMemory<byte>(original, 1, 4); // Offset = 1, Count = 4 (end of array) != Length 5

        var result = memory.ToByteArray();

        object.ReferenceEquals(result, original).Should().BeFalse();
        result.Should().Equal(new byte[] { 2, 3, 4, 5 });
    }

    [Fact]
    public void ToByteArray_EmptyMemory_ReturnsArray()
    {
        var memory = ReadOnlyMemory<byte>.Empty;

        var result = memory.ToByteArray();

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }
}
