// Copyright © Erickson Lopez. MIT License.
using Xunit;

namespace EricksonLopez.Outbox.Storage.PostgreSql.Tests;
[CollectionDefinition("PostgreSql")]
public class PostgreSqlCollection : ICollectionFixture<PostgreSqlContainerFixture>
{
}

