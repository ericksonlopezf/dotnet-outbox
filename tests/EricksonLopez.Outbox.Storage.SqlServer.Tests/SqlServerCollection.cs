// Copyright © Erickson Lopez. MIT License.
using Xunit;

namespace EricksonLopez.Outbox.Storage.SqlServer.Tests;
[CollectionDefinition("SqlServer")]
public class SqlServerCollection : ICollectionFixture<SqlServerContainerFixture>
{
}

