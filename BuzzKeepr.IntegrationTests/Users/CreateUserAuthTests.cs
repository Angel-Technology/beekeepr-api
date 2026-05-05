using BuzzKeepr.IntegrationTests.Common;

namespace BuzzKeepr.IntegrationTests.Users;

[Collection(IntegrationTestCollection.Name)]
public sealed class CreateUserAuthTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string ConfiguredKey = "test-app-api-key";
    private readonly BuzzKeeprApiFactory factory = new(postgres, ConfiguredKey);

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync()
    {
        factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CreateUser_WithoutHeader_WhenKeyConfigured_ReturnsForbiddenError()
    {
        var graphql = new GraphQLClient(factory.CreateClient());
        var email = $"key-{Guid.NewGuid():N}@buzzkeepr.test";

        var response = await graphql.SendAsync<CreateUserData>(
            "mutation($input: CreateUserInput!) { createUser(input: $input) { user { id } error } }",
            new { input = new { email } });

        var payload = response.RequireData().CreateUser;
        Assert.Null(payload.User);
        Assert.Equal("Forbidden: missing or invalid X-App-Api-Key header.", payload.Error);
    }

    [Fact]
    public async Task CreateUser_WithWrongHeader_ReturnsForbiddenError()
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add("X-App-Api-Key", "wrong-key");
        var graphql = new GraphQLClient(http);
        var email = $"key-wrong-{Guid.NewGuid():N}@buzzkeepr.test";

        var response = await graphql.SendAsync<CreateUserData>(
            "mutation($input: CreateUserInput!) { createUser(input: $input) { user { id } error } }",
            new { input = new { email } });

        Assert.Equal("Forbidden: missing or invalid X-App-Api-Key header.", response.RequireData().CreateUser.Error);
    }

    [Fact]
    public async Task CreateUser_WithMatchingHeader_Succeeds()
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add("X-App-Api-Key", ConfiguredKey);
        var graphql = new GraphQLClient(http);
        var email = $"key-good-{Guid.NewGuid():N}@buzzkeepr.test";

        var response = await graphql.SendAsync<CreateUserData>(
            "mutation($input: CreateUserInput!) { createUser(input: $input) { user { id email } error } }",
            new { input = new { email, displayName = "Allowed" } });

        var payload = response.RequireData().CreateUser;
        Assert.Null(payload.Error);
        Assert.Equal(email, payload.User!.Email);
    }

    private sealed record CreateUserData(CreateUserPayload CreateUser);
    private sealed record CreateUserPayload(CreatedUser? User, string? Error);
    private sealed record CreatedUser(Guid Id, string Email);
}
