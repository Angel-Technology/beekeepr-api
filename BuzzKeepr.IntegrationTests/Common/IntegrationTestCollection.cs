namespace BuzzKeepr.IntegrationTests.Common;

[CollectionDefinition(Name)]
public sealed class IntegrationTestCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Integration";
}
