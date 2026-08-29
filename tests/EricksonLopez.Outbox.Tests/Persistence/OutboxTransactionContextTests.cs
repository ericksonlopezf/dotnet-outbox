// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using EricksonLopez.Outbox.Persistence;
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

    [Fact]
    public void GenericConstructor_Should_Set_Properties()
    {
        var connection = "conn-obj";
        var transaction = "tx-obj";

        var context = new OutboxTransactionContext<string, string>(connection, transaction);

        context.Connection.Should().Be("conn-obj");
        context.Transaction.Should().Be("tx-obj");
        ((IOutboxTransactionContext)context).Connection.Should().Be("conn-obj");
        ((IOutboxTransactionContext)context).Transaction.Should().Be("tx-obj");
    }

    [Fact]
    public void GenericConstructor_AllowsNullConnection_ThrowsOnNullTransaction()
    {
        var transaction = "tx-obj";
        var context = new OutboxTransactionContext<string, string>(null, transaction);
        context.Connection.Should().BeNull();
        context.Transaction.Should().Be("tx-obj");

        var act = () => new OutboxTransactionContext<string, string>("conn", null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("transaction");
    }

    [Fact]
    public void ToOutboxContext_Extension_Creates_Generic_Context()
    {
        var transaction = "tx-obj";
        var connection = "conn-obj";

        var context = transaction.ToOutboxContext(connection);
        context.Should().NotBeNull();
        context.Connection.Should().Be("conn-obj");
        context.Transaction.Should().Be("tx-obj");

        Action actNull = () => OutboxGenericTransactionContextExtensions.ToOutboxContext<string, string>(null!, connection);
        actNull.Should().Throw<ArgumentNullException>().WithParameterName("transaction");
    }
}



