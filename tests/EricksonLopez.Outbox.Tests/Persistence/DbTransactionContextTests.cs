// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data.Common;
using AwesomeAssertions;
using EricksonLopez.Outbox.Persistence;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Persistence;

public class DbTransactionContextTests
{
    [Fact]
    public void Constructor_SetsDbTransaction()
    {
        var dbTransaction = Substitute.For<DbTransaction>();
        
        var sut = new DbTransactionContext(dbTransaction);
        
        sut.DbTransaction.Should().BeSameAs(dbTransaction);
        sut.Transaction.Should().BeSameAs(dbTransaction);
    }

    [Fact]
    public void DbConnection_ReturnsTransactionConnection()
    {
        var dbConnection = Substitute.For<DbConnection>();
        var dbTransaction = Substitute.For<DbTransaction>();
        dbTransaction.Connection.Returns(dbConnection);
        
        var sut = new DbTransactionContext(dbTransaction);
        
        sut.DbConnection.Should().BeSameAs(dbConnection);
        sut.Connection.Should().BeSameAs(dbConnection);
    }

    [Fact]
    public void GetContext_ReturnsCorrectType()
    {
        var dbTransaction = Substitute.For<DbTransaction>();
        
        var sut = new DbTransactionContext(dbTransaction);
        
        var context = ((IOutboxTransactionContext)sut).GetContext<DbTransaction>();
        
        context.Should().BeSameAs(dbTransaction);
    }

    [Fact]
    public void GetContext_WhenCastFails_ReturnsNull()
    {
        var dbTransaction = Substitute.For<DbTransaction>();
        
        var sut = new DbTransactionContext(dbTransaction);
        
        var context = ((IOutboxTransactionContext)sut).GetContext<string>();
        
        context.Should().BeNull();
    }

    [Fact]
    public void DbConnection_WhenTransactionIsNull_ReturnsNull()
    {
        var sut = new DbTransactionContext(null!);
        
        sut.DbConnection.Should().BeNull();
        sut.Connection.Should().BeNull();
    }
}

