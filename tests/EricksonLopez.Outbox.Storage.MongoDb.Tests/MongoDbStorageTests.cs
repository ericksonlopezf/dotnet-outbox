// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Storage.MongoDb;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Storage.MongoDb;

public class MongoDbStorageTests
{
    [Fact]
    public void Constructor_With_Null_Database_Throws_ArgumentNullException()
    {
        var act = () => new MongoDbOutboxRepository(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("database");

        var actDlq = () => new MongoDbDeadLetterRepository(null!);
        actDlq.Should().Throw<ArgumentNullException>().WithParameterName("database");
    }

    [Fact]
    public void AddMongoDbOutbox_NullGuards()
    {
        Action act1 = () => MongoDbOutboxExtensions.AddMongoDbOutbox(null!, _ => Substitute.For<IMongoDatabase>());
        act1.Should().Throw<ArgumentNullException>().WithParameterName("services");

        var services = new ServiceCollection();
        Action act2 = () => services.AddMongoDbOutbox(null!);
        act2.Should().Throw<ArgumentNullException>().WithParameterName("databaseFactory");
    }

    [Fact]
    public void AddMongoDbOutbox_Registers_Repositories_In_DI()
    {
        var services = new ServiceCollection();
        var mockDb = Substitute.For<IMongoDatabase>();
        var mockCollection = Substitute.For<IMongoCollection<BsonDocument>>();
        mockDb.GetCollection<BsonDocument>(Arg.Any<string>(), Arg.Any<MongoCollectionSettings>()).Returns(mockCollection);

        services.AddMongoDbOutbox(_ => mockDb);

        var provider = services.BuildServiceProvider();
        var outboxRepo = provider.GetService<IOutboxRepository>();
        var dlqRepo = provider.GetService<IDeadLetterRepository>();

        outboxRepo.Should().NotBeNull();
        outboxRepo.Should().BeOfType<MongoDbOutboxRepository>();
        dlqRepo.Should().NotBeNull();
        dlqRepo.Should().BeOfType<MongoDbDeadLetterRepository>();
    }

    [Fact]
    public void MongoDbTransactionContext_NullSession_ThrowsArgumentNullException()
    {
        Action act = () => { _ = new MongoDbTransactionContext(null!); };
        act.Should().Throw<ArgumentNullException>().WithParameterName("session");
    }

    [Fact]
    public void MongoDbTransactionContext_Properties()
    {
        var mockSession = Substitute.For<IClientSessionHandle>();
        var mockClient = Substitute.For<IMongoClient>();
        mockSession.Client.Returns(mockClient);

        var context = new MongoDbTransactionContext(mockSession);
        context.Session.Should().BeSameAs(mockSession);
        context.Transaction.Should().BeSameAs(mockSession);
        context.Connection.Should().BeSameAs(mockClient);
    }

    [Fact]
    public async Task MongoDbTransactionContext_Delegates_To_Session_When_In_Transaction()
    {
        var mockSession = Substitute.For<IClientSessionHandle>();
        mockSession.IsInTransaction.Returns(true);

        var context = new MongoDbTransactionContext(mockSession);

        await context.CommitAsync(CancellationToken.None);
        await mockSession.Received(1).CommitTransactionAsync(Arg.Any<CancellationToken>());

        await context.RollbackAsync(CancellationToken.None);
        await mockSession.Received(1).AbortTransactionAsync(Arg.Any<CancellationToken>());

        await context.DisposeAsync();
        mockSession.Received(1).Dispose();

        context.Dispose();
        mockSession.Received(2).Dispose();
    }

    [Fact]
    public async Task MongoDbTransactionContext_NoOp_When_Not_In_Transaction()
    {
        var mockSession = Substitute.For<IClientSessionHandle>();
        mockSession.IsInTransaction.Returns(false);

        var context = new MongoDbTransactionContext(mockSession);

        await context.CommitAsync(CancellationToken.None);
        await mockSession.DidNotReceive().CommitTransactionAsync(Arg.Any<CancellationToken>());

        await context.RollbackAsync(CancellationToken.None);
        await mockSession.DidNotReceive().AbortTransactionAsync(Arg.Any<CancellationToken>());
    }
}
