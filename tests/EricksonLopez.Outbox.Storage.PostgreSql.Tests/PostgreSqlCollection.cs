using Xunit;
namespace EricksonLopez.Outbox.Tests;
[CollectionDefinition("PostgreSql")]
public class PostgreSqlCollection : ICollectionFixture<PostgreSqlContainerFixture>
{
}
