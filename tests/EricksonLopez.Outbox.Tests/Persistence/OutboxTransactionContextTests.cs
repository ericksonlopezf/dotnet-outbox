using System;
using EricksonLopez.Outbox.Persistence;
using AwesomeAssertions;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Persistence;

public class OutboxTransactionContextTests
{
    [Fact]
    public void Constructor_Should_Set_Properties()
    {
        var connection = new object();
        var transaction = new object();

        var context = new OutboxTransactionContext(connection, transaction);

        context.Connection.Should().BeSameAs(connection);
        context.Transaction.Should().BeSameAs(transaction);
    }

    [Fact]
    public void Constructor_Should_Throw_When_Connection_Is_Null()
    {
        var transaction = new object();
        
        var act = () => new OutboxTransactionContext(null!, transaction);

        act.Should().Throw<ArgumentNullException>().WithParameterName("connection");
    }

    [Fact]
    public void Constructor_Should_Throw_When_Transaction_Is_Null()
    {
        var connection = new object();
        
        var act = () => new OutboxTransactionContext(connection, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("transaction");
    }
}


