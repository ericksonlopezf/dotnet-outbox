using Xunit;
namespace EricksonLopez.Outbox.Tests;
[CollectionDefinition("SqlServer")]
public class SqlServerCollection : ICollectionFixture<SqlServerContainerFixture>
{
}
