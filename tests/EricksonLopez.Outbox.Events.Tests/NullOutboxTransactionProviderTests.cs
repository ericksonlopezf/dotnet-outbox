// Copyright © Erickson Lopez. MIT License.
using AwesomeAssertions;
using EricksonLopez.Outbox.Events;
using Xunit;

namespace EricksonLopez.Outbox.Events.Tests;

[Trait("Category", "Unit")]
public sealed class NullOutboxTransactionProviderTests
{
    [Fact]
    public void Instance_ReturnsSingletonNonNullInstance()
    {
        var instance = NullOutboxTransactionProvider.Instance;
        instance.Should().NotBeNull();
        NullOutboxTransactionProvider.Instance.Should().BeSameAs(instance);
    }

    [Fact]
    public void CurrentTransaction_ReturnsNull()
    {
        var instance = NullOutboxTransactionProvider.Instance;
        instance.CurrentTransaction.Should().BeNull();
    }
}
