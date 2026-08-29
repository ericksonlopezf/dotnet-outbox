// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;

namespace EricksonLopez.Outbox.Testing;

internal sealed class FakeBrokerAssertion : IFakeBrokerAssertion
{
    private readonly IEnumerable<PublishedRawMessage> _captured;
    private readonly string _typeAlias;
    private string? _correlationId;

    public FakeBrokerAssertion(IEnumerable<PublishedRawMessage> captured, string typeAlias)
    {
        _captured = captured;
        _typeAlias = typeAlias;
    }

    public IFakeBrokerAssertion WithCorrelationId(string correlationId)
    {
        _correlationId = correlationId;
        return this;
    }

    public void Once() => Times(1);

    public void Times(int count)
    {
        var actual = CountMatching();
        if (actual != count)
            throw new InvalidOperationException(
                $"Expected '{_typeAlias}' to be published {count} time(s), but was {actual}.");
    }

    public void AtLeastOnce()
    {
        if (CountMatching() == 0)
            throw new InvalidOperationException(
                $"Expected '{_typeAlias}' to be published at least once, but it was never published.");
    }

    public void Never()
    {
        var count = CountMatching();
        if (count > 0)
            throw new InvalidOperationException(
                $"Expected '{_typeAlias}' to never be published, but it was published {count} time(s).");
    }

    private int CountMatching()
    {
        int count = 0;
        foreach (var msg in _captured)
        {
            if (!string.Equals(msg.MessageType, _typeAlias, StringComparison.OrdinalIgnoreCase))
                continue;

            if (_correlationId is not null
                && !string.Equals(msg.Metadata.CorrelationId, _correlationId, StringComparison.Ordinal))
                continue;

            count++;
        }
        return count;
    }
}
