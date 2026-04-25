using System.Net;
using System.Net.Http.Json;
using BuzzKeepr.Infrastructure.Persistence;
using BuzzKeepr.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BuzzKeepr.IntegrationTests.Auth;

[Collection(IntegrationTestCollection.Name)]
public sealed class EmailSignInTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly BuzzKeeprApiFactory factory = new(postgres);

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync()
    {
        factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task RequestThenVerify_HappyPath_CreatesUserAndIssuesSessionCookie()
    {
        var email = $"signin-{Guid.NewGuid():N}@buzzkeepr.test";
        var http = factory.CreateClient();
        var graphql = new GraphQLClient(http);

        var requestResponse = await graphql.SendAsync<RequestEmailSignInData>(
            "mutation($input: RequestEmailSignInInput!) { requestEmailSignIn(input: $input) { success email error } }",
            new { input = new { email } });

        Assert.True(requestResponse.RequireData().RequestEmailSignIn.Success);

        var sentCode = factory.FakeEmailSender.RequireLatestFor(email);

        var verifyRaw = await graphql.SendRawAsync(
            "mutation($input: VerifyEmailSignInInput!) { verifyEmailSignIn(input: $input) { user { id email emailVerified } session { token expiresAtUtc } error } }",
            new { input = new { email, code = sentCode.Code } });

        Assert.Equal(HttpStatusCode.OK, verifyRaw.StatusCode);
        Assert.True(verifyRaw.Headers.TryGetValues("Set-Cookie", out var setCookies));
        Assert.Contains(setCookies!, value => value.StartsWith("buzzkeepr_session=", StringComparison.Ordinal));

        var verify = (await verifyRaw.Content.ReadFromJsonAsync<GraphQLResponse<VerifyEmailSignInData>>())!
            .RequireData().VerifyEmailSignIn;
        Assert.Null(verify.Error);
        Assert.Equal(email, verify.User!.Email);
        Assert.True(verify.User.EmailVerified);
        Assert.False(string.IsNullOrEmpty(verify.Session!.Token));
    }

    [Fact]
    public async Task Verify_WithWrongCode_ReturnsInvalidTokenError()
    {
        var email = $"wrongcode-{Guid.NewGuid():N}@buzzkeepr.test";
        var http = factory.CreateClient();
        var graphql = new GraphQLClient(http);

        await graphql.SendAsync<RequestEmailSignInData>(
            "mutation($input: RequestEmailSignInInput!) { requestEmailSignIn(input: $input) { success } }",
            new { input = new { email } });

        var verifyResponse = await graphql.SendAsync<VerifyEmailSignInData>(
            "mutation($input: VerifyEmailSignInInput!) { verifyEmailSignIn(input: $input) { user { id } session { token } error } }",
            new { input = new { email, code = "00000" } });

        var verify = verifyResponse.RequireData().VerifyEmailSignIn;
        Assert.Equal("Invalid or expired token.", verify.Error);
        Assert.Null(verify.User);
        Assert.Null(verify.Session);
    }

    [Fact]
    public async Task Verify_WithFiveWrongCodesThenRightCode_StillReturnsInvalidToken()
    {
        var email = $"lockout-{Guid.NewGuid():N}@buzzkeepr.test";
        var http = factory.CreateClient();
        var graphql = new GraphQLClient(http);

        await graphql.SendAsync<RequestEmailSignInData>(
            "mutation($input: RequestEmailSignInInput!) { requestEmailSignIn(input: $input) { success } }",
            new { input = new { email } });
        var realCode = factory.FakeEmailSender.RequireLatestFor(email).Code;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var wrong = await graphql.SendAsync<VerifyEmailSignInData>(
                "mutation($input: VerifyEmailSignInInput!) { verifyEmailSignIn(input: $input) { user { id } error } }",
                new { input = new { email, code = "00000" } });
            Assert.Equal("Invalid or expired token.", wrong.RequireData().VerifyEmailSignIn.Error);
        }

        var afterLockout = await graphql.SendAsync<VerifyEmailSignInData>(
            "mutation($input: VerifyEmailSignInInput!) { verifyEmailSignIn(input: $input) { user { id email emailVerified } error } }",
            new { input = new { email, code = realCode } });

        Assert.Equal("Invalid or expired token.", afterLockout.RequireData().VerifyEmailSignIn.Error);
        Assert.Null(afterLockout.RequireData().VerifyEmailSignIn.User);
    }

    [Fact]
    public async Task SignOut_RevokesSessionAndClearsCookie()
    {
        var email = $"signout-{Guid.NewGuid():N}@buzzkeepr.test";
        var http = factory.CreateClient();
        var graphql = new GraphQLClient(http);

        await graphql.SendAsync<RequestEmailSignInData>(
            "mutation($input: RequestEmailSignInInput!) { requestEmailSignIn(input: $input) { success } }",
            new { input = new { email } });
        var code = factory.FakeEmailSender.RequireLatestFor(email).Code;
        var verify = (await graphql.SendAsync<VerifyEmailSignInData>(
            "mutation($input: VerifyEmailSignInInput!) { verifyEmailSignIn(input: $input) { session { token } error } }",
            new { input = new { email, code } })).RequireData().VerifyEmailSignIn;

        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", verify.Session!.Token);

        var signOutResponse = await graphql.SendAsync<SignOutData>(
            "mutation { signOut { success } }");

        Assert.True(signOutResponse.RequireData().SignOut.Success);

        var currentUserAfter = await graphql.SendAsync<CurrentUserData>(
            "query { currentUser { id } }");
        Assert.Null(currentUserAfter.RequireData().CurrentUser);
    }

    private sealed record RequestEmailSignInData(RequestEmailSignInPayload RequestEmailSignIn);
    private sealed record RequestEmailSignInPayload(bool Success, string? Email, string? Error);

    private sealed record VerifyEmailSignInData(VerifyEmailSignInPayload VerifyEmailSignIn);
    private sealed record VerifyEmailSignInPayload(VerifiedUser? User, VerifiedSession? Session, string? Error);
    private sealed record VerifiedUser(Guid Id, string Email, bool EmailVerified);
    private sealed record VerifiedSession(string Token, DateTime ExpiresAtUtc);

    private sealed record SignOutData(SignOutPayload SignOut);
    private sealed record SignOutPayload(bool Success);

    private sealed record CurrentUserData(CurrentUser? CurrentUser);
    private sealed record CurrentUser(Guid Id);
}
