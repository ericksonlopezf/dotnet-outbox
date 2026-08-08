using System;
using System.Data.Common;
using AwesomeAssertions;
using EricksonLopez.Outbox.Persistence;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Persistence;

public class OutboxTransactionContextExtensionsTests
{
    [Fact]
    public void ToOutboxContext_NullTransaction_ThrowsArgumentNullException()
    {
        DbTransaction nullTransaction = null!;
        Action act = () => nullTransaction.ToOutboxContext();
        act.Should().Throw<ArgumentNullException>().WithParameterName("transaction");
    }

    [Fact]
    public void ToOutboxContext_ValidTransaction_ReturnsDbTransactionContext()
    {
        var transaction = Substitute.For<DbTransaction>();
        var result = transaction.ToOutboxContext();
        
        result.Should().NotBeNull();
        result.Should().BeOfType<DbTransactionContext>();
        
        var dbContext = (DbTransactionContext)result;
        dbContext.DbTransaction.Should().BeSameAs(transaction);
        dbContext.Transaction.Should().BeSameAs(transaction);
    }
}
