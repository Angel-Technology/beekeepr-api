using System.Text.Json;
using BuzzKeepr.Application.Auth.Models;
using BuzzKeepr.Domain.Enums;
using BuzzKeepr.Infrastructure.Persistence;
using BuzzKeepr.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BuzzKeepr.IntegrationTests.Auth;

[Collection(IntegrationTestCollection.Name)]
public sealed class AppleSignInTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly BuzzKeeprApiFactory factory = new(postgres);

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync()
    {
        factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SignInWithApple_NewUser_CreatesUserAndExternalAccountWithClientProvidedName()
    {
        const string idToken = "apple-id-token-new";
        var email = $"apple-{Guid.NewGuid():N}@buzzkeepr.test";
        factory.FakeAppleVerifier.RegisterValidToken(idToken, new AppleIdentity
        {
            ProviderAccountId = $"apple-sub-{Guid.NewGuid():N}",
            Email = email,
            EmailVerified = true
        });

        var graphql = new GraphQLClient(factory.CreateClient());

        var response = await graphql.SendAsync<SignInWithAppleData>(
            "mutation($input: SignInWithAppleInput!) { signInWithApple(input: $input) { user { id email displayName emailVerified } session { token expiresAtUtc } error } }",
            new { input = new { idToken, displayName = "Jane Apple" } });

        var payload = response.RequireData().SignInWithApple;
        Assert.Null(payload.Error);
        Assert.Equal(email, payload.User!.Email);
        Assert.True(payload.User.EmailVerified);
        Assert.Equal("Jane Apple", payload.User.DisplayName);
        Assert.False(string.IsNullOrEmpty(payload.Session!.Token));

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();

        var user = await dbContext.Users
            .Include(u => u.ExternalAccounts)
            .AsNoTracking()
            .FirstAsync(u => u.Email == email);

        var externalAccount = Assert.Single(user.ExternalAccounts);
        Assert.Equal(AuthProvider.Apple, externalAccount.Provider);
        Assert.Equal(email, externalAccount.ProviderEmail);
        Assert.NotNull(externalAccount.LastSignInAtUtc);
    }

    [Fact]
    public async Task SignInWithApple_PrivateRelayEmail_StoredAsUserEmail()
    {
        const string idToken = "apple-id-token-relay";
        var email = $"abcd1234-{Guid.NewGuid():N}@privaterelay.appleid.com";
        factory.FakeAppleVerifier.RegisterValidToken(idToken, new AppleIdentity
        {
            ProviderAccountId = $"apple-sub-{Guid.NewGuid():N}",
            Email = email,
            EmailVerified = true,
            IsPrivateRelayEmail = true
        });

        var graphql = new GraphQLClient(factory.CreateClient());

        var response = await graphql.SendAsync<SignInWithAppleData>(
            "mutation($input: SignInWithAppleInput!) { signInWithApple(input: $input) { user { id email emailVerified } error } }",
            new { input = new { idToken } });

        var payload = response.RequireData().SignInWithApple;
        Assert.Null(payload.Error);
        Assert.Equal(email, payload.User!.Email);
        Assert.True(payload.User.EmailVerified);
    }

    [Fact]
    public async Task SignInWithApple_ExistingEmailUser_LinksWithoutDuplicating()
    {
        var email = $"link-apple-{Guid.NewGuid():N}@buzzkeepr.test";

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

        const string idToken = "apple-id-token-link";
        factory.FakeAppleVerifier.RegisterValidToken(idToken, new AppleIdentity
        {
            ProviderAccountId = $"apple-sub-{Guid.NewGuid():N}",
            Email = email,
            EmailVerified = true
        });

        var appleResponse = await graphql.SendAsync<SignInWithAppleData>(
            "mutation($input: SignInWithAppleInput!) { signInWithApple(input: $input) { user { id } error } }",
            new { input = new { idToken } });

        Assert.Null(appleResponse.RequireData().SignInWithApple.Error);
        Assert.Equal(existingUserId, appleResponse.RequireData().SignInWithApple.User!.Id);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();

        var matchingUsers = await dbContext.Users
            .Include(u => u.ExternalAccounts)
            .Where(u => u.Email == email)
            .ToListAsync();

        var user = Assert.Single(matchingUsers);
        var externalAccount = Assert.Single(user.ExternalAccounts);
        Assert.Equal(AuthProvider.Apple, externalAccount.Provider);
    }

    [Fact]
    public async Task SignInWithApple_SecondSignIn_DoesNotOverwriteExistingDisplayName()
    {
        const string firstToken = "apple-id-token-first";
        const string secondToken = "apple-id-token-second";
        var email = $"name-apple-{Guid.NewGuid():N}@buzzkeepr.test";
        var providerSubject = $"apple-sub-{Guid.NewGuid():N}";

        factory.FakeAppleVerifier.RegisterValidToken(firstToken, new AppleIdentity
        {
            ProviderAccountId = providerSubject,
            Email = email,
            EmailVerified = true
        });
        factory.FakeAppleVerifier.RegisterValidToken(secondToken, new AppleIdentity
        {
            ProviderAccountId = providerSubject,
            Email = email,
            EmailVerified = true
        });

        var graphql = new GraphQLClient(factory.CreateClient());

        // First sign-in: client forwards Apple's one-time name.
        await graphql.SendAsync<SignInWithAppleData>(
            "mutation($input: SignInWithAppleInput!) { signInWithApple(input: $input) { user { id displayName } error } }",
            new { input = new { idToken = firstToken, displayName = "Original Name" } });

        // Second sign-in: Apple omits the name; the client passes nothing.
        var second = await graphql.SendAsync<SignInWithAppleData>(
            "mutation($input: SignInWithAppleInput!) { signInWithApple(input: $input) { user { id displayName } error } }",
            new { input = new { idToken = secondToken } });

        Assert.Null(second.RequireData().SignInWithApple.Error);
        Assert.Equal("Original Name", second.RequireData().SignInWithApple.User!.DisplayName);
    }

    [Fact]
    public async Task SignInWithApple_InvalidIdToken_ReturnsInvalidTokenError()
    {
        const string idToken = "apple-id-token-rejected";
        factory.FakeAppleVerifier.RegisterInvalidToken(idToken);

        var graphql = new GraphQLClient(factory.CreateClient());

        var response = await graphql.SendAsync<SignInWithAppleData>(
            "mutation($input: SignInWithAppleInput!) { signInWithApple(input: $input) { user { id } session { token } error } }",
            new { input = new { idToken } });

        var payload = response.RequireData().SignInWithApple;
        Assert.Equal("Invalid Apple identity token.", payload.Error);
        Assert.Null(payload.User);
        Assert.Null(payload.Session);
    }

    private sealed record SignInWithAppleData(SignInWithApplePayload SignInWithApple);
    private sealed record SignInWithApplePayload(AppleUser? User, AppleSession? Session, string? Error);
    private sealed record AppleUser(Guid Id, string Email, string? DisplayName, bool EmailVerified);
    private sealed record AppleSession(string Token, DateTime ExpiresAtUtc);

    private sealed record VerifyEmailSignInData(VerifyEmailSignInPayload VerifyEmailSignIn);
    private sealed record VerifyEmailSignInPayload(VerifyUser? User);
    private sealed record VerifyUser(Guid Id);
}
