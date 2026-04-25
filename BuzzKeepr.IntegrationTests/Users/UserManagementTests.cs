using System.Text.Json;
using BuzzKeepr.Infrastructure.Persistence;
using BuzzKeepr.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BuzzKeepr.IntegrationTests.Users;

[Collection(IntegrationTestCollection.Name)]
public sealed class UserManagementTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly BuzzKeeprApiFactory factory = new(postgres);

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync()
    {
        factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CreateUser_WithDuplicateEmail_ReturnsEmailAlreadyExistsError()
    {
        var email = $"dup-{Guid.NewGuid():N}@buzzkeepr.test";
        var graphql = new GraphQLClient(factory.CreateClient());

        var first = await graphql.SendAsync<CreateUserData>(
            "mutation($input: CreateUserInput!) { createUser(input: $input) { user { id email } error } }",
            new { input = new { email, displayName = "First" } });
        Assert.Null(first.RequireData().CreateUser.Error);
        var firstId = first.RequireData().CreateUser.User!.Id;

        var second = await graphql.SendAsync<CreateUserData>(
            "mutation($input: CreateUserInput!) { createUser(input: $input) { user { id } error } }",
            new { input = new { email, displayName = "Second" } });
        Assert.Equal("A user with that email already exists.", second.RequireData().CreateUser.Error);
        Assert.Null(second.RequireData().CreateUser.User);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        var matching = await dbContext.Users.AsNoTracking().Where(u => u.Email == email).ToListAsync();
        var only = Assert.Single(matching);
        Assert.Equal(firstId, only.Id);
    }

    [Fact]
    public async Task AcceptTerms_WithoutSession_ReturnsAuthenticationRequiredError()
    {
        var graphql = new GraphQLClient(factory.CreateClient());

        var response = await graphql.SendAsync<AcceptTermsData>(
            "mutation { acceptTerms { user { id } error } }");

        var payload = response.RequireData().AcceptTerms;
        Assert.Equal("Authentication is required.", payload.Error);
        Assert.Null(payload.User);
    }

    [Fact]
    public async Task AcceptTerms_WithSession_PersistsTermsAcceptedAtUtc()
    {
        var (token, userId) = await SignInAsync();
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var graphql = new GraphQLClient(http);

        var before = DateTime.UtcNow.AddSeconds(-1);

        var response = await graphql.SendAsync<AcceptTermsData>(
            "mutation { acceptTerms { user { id termsAcceptedAtUtc } error } }");

        var payload = response.RequireData().AcceptTerms;
        Assert.Null(payload.Error);
        Assert.Equal(userId, payload.User!.Id);
        Assert.NotNull(payload.User.TermsAcceptedAtUtc);
        Assert.True(payload.User.TermsAcceptedAtUtc > before);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        var user = await dbContext.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
        Assert.NotNull(user.TermsAcceptedAtUtc);
        Assert.True(user.TermsAcceptedAtUtc > before);
    }

    private async Task<(string Token, Guid UserId)> SignInAsync()
    {
        var email = $"terms-{Guid.NewGuid():N}@buzzkeepr.test";
        var http = factory.CreateClient();
        var graphql = new GraphQLClient(http);

        await graphql.SendAsync<JsonElement>(
            "mutation($input: RequestEmailSignInInput!) { requestEmailSignIn(input: $input) { success } }",
            new { input = new { email } });
        var code = factory.FakeEmailSender.RequireLatestFor(email).Code;
        var verify = await graphql.SendAsync<VerifyData>(
            "mutation($input: VerifyEmailSignInInput!) { verifyEmailSignIn(input: $input) { user { id } session { token } } }",
            new { input = new { email, code } });
        var data = verify.RequireData().VerifyEmailSignIn;
        return (data.Session!.Token, data.User!.Id);
    }

    private sealed record CreateUserData(CreateUserPayload CreateUser);
    private sealed record CreateUserPayload(CreatedUser? User, string? Error);
    private sealed record CreatedUser(Guid Id, string Email);

    private sealed record AcceptTermsData(AcceptTermsPayload AcceptTerms);
    private sealed record AcceptTermsPayload(AcceptedUser? User, string? Error);
    private sealed record AcceptedUser(Guid Id, DateTime? TermsAcceptedAtUtc);

    private sealed record VerifyData(VerifyPayload VerifyEmailSignIn);
    private sealed record VerifyPayload(VerifyUser? User, VerifySession? Session);
    private sealed record VerifyUser(Guid Id);
    private sealed record VerifySession(string Token);
}
