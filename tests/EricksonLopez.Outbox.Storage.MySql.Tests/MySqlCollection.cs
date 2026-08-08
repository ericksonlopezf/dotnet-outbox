using Xunit;
namespace EricksonLopez.Outbox.Tests;
[CollectionDefinition("MySql")]
public class MySqlCollection : ICollectionFixture<MySqlContainerFixture>
{
}
