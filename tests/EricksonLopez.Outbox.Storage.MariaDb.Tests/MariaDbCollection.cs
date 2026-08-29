// Copyright © Erickson Lopez. MIT License.
using Xunit;

namespace EricksonLopez.Outbox.Storage.MariaDb.Tests;

[CollectionDefinition("MariaDb")]
public class MariaDbCollection : ICollectionFixture<MariaDbContainerFixture>
{
}
