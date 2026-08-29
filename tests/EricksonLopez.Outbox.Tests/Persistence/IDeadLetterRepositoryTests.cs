// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox.Persistence;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Persistence;

public class IDeadLetterRepositoryTests
{
    private sealed class DefaultDeadLetterRepository : IDeadLetterRepository
    {
        public ValueTask InsertAsync(DeadLetterMessage message, IOutboxTransactionContext? transaction = null, CancellationToken cancellationToken = default) => default;
        public ValueTask<IReadOnlyList<DeadLetterMessage>> GetAsync(int limit = 100, DateTimeOffset? after = null, CancellationToken cancellationToken = default) => default;
        public ValueTask DeleteAsync(Guid id, CancellationToken cancellationToken = default) => default;
        public ValueTask PurgeAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default) => default;
    }

    [Fact]
    public void IsFirstPartyImplementation_HasDefaultImplementation_ReturnsFalse()
    {
        IDeadLetterRepository repo = new DefaultDeadLetterRepository();
        repo.IsFirstPartyImplementation.Should().BeFalse();
    }
}



