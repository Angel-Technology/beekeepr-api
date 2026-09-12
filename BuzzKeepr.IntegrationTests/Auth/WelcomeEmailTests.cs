using System.Text.Json;
using BuzzKeepr.Application.Auth.Models;
using BuzzKeepr.Infrastructure.Persistence;
using BuzzKeepr.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BuzzKeepr.IntegrationTests.Auth;

[Collection(IntegrationTestCollection.Name)]
public sealed class WelcomeEmailTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly BuzzKeeprApiFactory factory = new(postgres);

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync()
    {
        factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task EmailSignIn_NewUser_SendsWelcomeImmediately()
    {
        var email = $"welcome-email-{Guid.NewGuid():N}@buzzkeepr.test";
        var graphql = new GraphQLClient(factory.CreateClient());

        await graphql.SendAsync<JsonElement>(
            "mutation($input: RequestEmailSignInInput!) { requestEmailSignIn(input: $input) { success } }",
            new { input = new { email } });
        var code = factory.FakeEmailSender.RequireLatestFor(email).Code;
        var verify = await graphql.SendAsync<VerifyData>(
            "mutation($input: VerifyEmailSignInInput!) { verifyEmailSignIn(input: $input) { user { id } } }",
            new { input = new { email, code } });
        var userId = verify.RequireData().VerifyEmailSignIn.User!.Id;

        Assert.Contains(factory.FakeWelcomeSender.Sent, w => w.Email == email);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        var user = await dbContext.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
        Assert.NotNull(user.WelcomeEmailSentAtUtc);
    }

    [Fact]
    public async Task EmailSignIn_ReturningUser_DoesNotSendWelcomeAgain()
    {
        var email = $"welcome-returning-{Guid.NewGuid():N}@buzzkeepr.test";
        var graphql = new GraphQLClient(factory.CreateClient());

        await graphql.SendAsync<JsonElement>(
            "mutation($input: RequestEmailSignInInput!) { requestEmailSignIn(input: $input) { success } }",
            new { input = new { email } });
        var code1 = factory.FakeEmailSender.RequireLatestFor(email).Code;
        await graphql.SendAsync<VerifyData>(
            "mutation($input: VerifyEmailSignInInput!) { verifyEmailSignIn(input: $input) { user { id } } }",
            new { input = new { email, code = code1 } });

        var sentAfterFirst = factory.FakeWelcomeSender.Sent.Count(w => w.Email == email);
        Assert.Equal(1, sentAfterFirst);

        await graphql.SendAsync<JsonElement>(
            "mutation($input: RequestEmailSignInInput!) { requestEmailSignIn(input: $input) { success } }",
            new { input = new { email } });
        var code2 = factory.FakeEmailSender.RequireLatestFor(email).Code;
        await graphql.SendAsync<VerifyData>(
            "mutation($input: VerifyEmailSignInInput!) { verifyEmailSignIn(input: $input) { user { id } } }",
            new { input = new { email, code = code2 } });

        Assert.Equal(sentAfterFirst, factory.FakeWelcomeSender.Sent.Count(w => w.Email == email));
    }

    [Fact]
    public async Task GoogleSignIn_NewUser_SendsWelcomeImmediately()
    {
        const string idToken = "google-token-welcome";
        var email = $"welcome-google-{Guid.NewGuid():N}@buzzkeepr.test";
        factory.FakeGoogleVerifier.RegisterValidToken(idToken, new GoogleIdentity
        {
            ProviderAccountId = $"google-{Guid.NewGuid():N}",
            Email = email,
            DisplayName = "Welcome Tester"
        });

        var graphql = new GraphQLClient(factory.CreateClient());

        await graphql.SendAsync<JsonElement>(
            "mutation($input: SignInWithGoogleInput!) { signInWithGoogle(input: $input) { user { id } error } }",
            new { input = new { idToken } });

        Assert.Contains(factory.FakeWelcomeSender.Sent, w => w.Email == email);
    }

    [Fact]
    public async Task CreateUser_SendsWelcomeImmediatelyWithoutDisplayName()
    {
        // Signup mutation takes email only — no DisplayName. Welcome still fires; the sender
        // falls back to the "Newbee" generic greeting.
        var email = $"welcome-create-{Guid.NewGuid():N}@buzzkeepr.test";
        var graphql = new GraphQLClient(factory.CreateClient());

        var create = await graphql.SendAsync<CreateUserData>(
            "mutation($input: CreateUserInput!) { createUser(input: $input) { user { id } error } }",
            new { input = new { email } });

        var userId = create.RequireData().CreateUser.User!.Id;
        var welcome = Assert.Single(factory.FakeWelcomeSender.Sent, w => w.Email == email);
        Assert.Null(welcome.DisplayName);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        var user = await dbContext.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
        Assert.NotNull(user.WelcomeEmailSentAtUtc);
    }

    private sealed record VerifyData(VerifyPayload VerifyEmailSignIn);
    private sealed record VerifyPayload(VerifyUser? User, string? Error);
    private sealed record VerifyUser(Guid Id, string Email);

    private sealed record CreateUserData(CreateUserPayload CreateUser);
    private sealed record CreateUserPayload(CreatedUser? User, string? Error);
    private sealed record CreatedUser(Guid Id);
}
