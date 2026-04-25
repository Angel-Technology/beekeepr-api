using System.Text.Json;
using BuzzKeepr.Application.Auth.Models;
using BuzzKeepr.Domain.Enums;
using BuzzKeepr.Infrastructure.Persistence;
using BuzzKeepr.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BuzzKeepr.IntegrationTests.Auth;

[Collection(IntegrationTestCollection.Name)]
public sealed class GoogleSignInTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly BuzzKeeprApiFactory factory = new(postgres);

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync()
    {
        factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SignInWithGoogle_NewUser_CreatesUserAndExternalAccount()
    {
        const string idToken = "google-id-token-new";
        var email = $"google-{Guid.NewGuid():N}@buzzkeepr.test";
        factory.FakeGoogleVerifier.RegisterValidToken(idToken, new GoogleIdentity
        {
            ProviderAccountId = $"google-acct-{Guid.NewGuid():N}",
            Email = email,
            DisplayName = "Jane Google"
        });

        var graphql = new GraphQLClient(factory.CreateClient());

        var response = await graphql.SendAsync<SignInWithGoogleData>(
            "mutation($input: SignInWithGoogleInput!) { signInWithGoogle(input: $input) { user { id email displayName emailVerified } session { token expiresAtUtc } error } }",
            new { input = new { idToken } });

        var payload = response.RequireData().SignInWithGoogle;
        Assert.Null(payload.Error);
        Assert.Equal(email, payload.User!.Email);
        Assert.True(payload.User.EmailVerified);
        Assert.Equal("Jane Google", payload.User.DisplayName);
        Assert.False(string.IsNullOrEmpty(payload.Session!.Token));

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();

        var user = await dbContext.Users
            .Include(u => u.ExternalAccounts)
            .AsNoTracking()
            .FirstAsync(u => u.Email == email);

        var externalAccount = Assert.Single(user.ExternalAccounts);
        Assert.Equal(AuthProvider.Google, externalAccount.Provider);
        Assert.Equal(email, externalAccount.ProviderEmail);
        Assert.NotNull(externalAccount.LastSignInAtUtc);
    }

    [Fact]
    public async Task SignInWithGoogle_ExistingEmailUser_LinksWithoutDuplicating()
    {
        var email = $"link-{Guid.NewGuid():N}@buzzkeepr.test";

        var http = factory.CreateClient();
        var graphql = new GraphQLClient(http);

        await graphql.SendAsync<JsonElement>(
            "mutation($input: RequestEmailSignInInput!) { requestEmailSignIn(input: $input) { success } }",
            new { input = new { email } });
        var code = factory.FakeEmailSender.RequireLatestFor(email).Code;
        var emailVerify = await graphql.SendAsync<VerifyEmailSignInData>(
            "mutation($input: VerifyEmailSignInInput!) { verifyEmailSignIn(input: $input) { user { id } } }",
            new { input = new { email, code } });
        var existingUserId = emailVerify.RequireData().VerifyEmailSignIn.User!.Id;

        const string idToken = "google-id-token-link";
        factory.FakeGoogleVerifier.RegisterValidToken(idToken, new GoogleIdentity
        {
            ProviderAccountId = $"google-acct-{Guid.NewGuid():N}",
            Email = email,
            DisplayName = null
        });

        var googleResponse = await graphql.SendAsync<SignInWithGoogleData>(
            "mutation($input: SignInWithGoogleInput!) { signInWithGoogle(input: $input) { user { id } error } }",
            new { input = new { idToken } });

        Assert.Null(googleResponse.RequireData().SignInWithGoogle.Error);
        Assert.Equal(existingUserId, googleResponse.RequireData().SignInWithGoogle.User!.Id);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();

        var matchingUsers = await dbContext.Users
            .Include(u => u.ExternalAccounts)
            .Where(u => u.Email == email)
            .ToListAsync();

        var user = Assert.Single(matchingUsers);
        var externalAccount = Assert.Single(user.ExternalAccounts);
        Assert.Equal(AuthProvider.Google, externalAccount.Provider);
    }

    [Fact]
    public async Task SignInWithGoogle_InvalidIdToken_ReturnsInvalidTokenError()
    {
        const string idToken = "google-id-token-rejected";
        factory.FakeGoogleVerifier.RegisterInvalidToken(idToken);

        var graphql = new GraphQLClient(factory.CreateClient());

        var response = await graphql.SendAsync<SignInWithGoogleData>(
            "mutation($input: SignInWithGoogleInput!) { signInWithGoogle(input: $input) { user { id } session { token } error } }",
            new { input = new { idToken } });

        var payload = response.RequireData().SignInWithGoogle;
        Assert.Equal("Invalid Google ID token.", payload.Error);
        Assert.Null(payload.User);
        Assert.Null(payload.Session);
    }

    private sealed record SignInWithGoogleData(SignInWithGooglePayload SignInWithGoogle);
    private sealed record SignInWithGooglePayload(GoogleUser? User, GoogleSession? Session, string? Error);
    private sealed record GoogleUser(Guid Id, string Email, string? DisplayName, bool EmailVerified);
    private sealed record GoogleSession(string Token, DateTime ExpiresAtUtc);

    private sealed record VerifyEmailSignInData(VerifyEmailSignInPayload VerifyEmailSignIn);
    private sealed record VerifyEmailSignInPayload(VerifyUser? User);
    private sealed record VerifyUser(Guid Id);
}
