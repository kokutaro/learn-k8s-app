namespace OsoujiSystem.WebApi.Tests;

[CollectionDefinition(Name)]
public sealed class ApiIntegrationTestCollection : ICollectionFixture<ApiIntegrationTestFixture>
{
    public const string Name = "api-integration";
}
