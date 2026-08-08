using Xunit;
namespace EricksonLopez.Outbox.Tests;
[CollectionDefinition("Oracle")]
public class OracleCollection : ICollectionFixture<OracleContainerFixture>
{
}
