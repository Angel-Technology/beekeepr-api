using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BuzzKeepr.Infrastructure.Persistence;
using BuzzKeepr.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BuzzKeepr.IntegrationTests.Auth;

[Collection(IntegrationTestCollection.Name)]
public sealed class SessionTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly BuzzKeeprApiFactory factory = new(postgres);

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync()
    {
        factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CurrentUser_WhenLastSeenOlderThan24h_ExtendsExpiry()
    {
        var (token, sessionId, originalExpiresAt) = await SignInAsync();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
            await dbContext.Sessions
                .Where(session => session.Id == sessionId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(session => session.LastSeenAtUtc, DateTime.UtcNow.AddHours(-25)));
        }

        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var graphql = new GraphQLClient(http);

        var response = await graphql.SendAsync<CurrentUserData>("query { currentUser { id } }");
        Assert.NotNull(response.RequireData().CurrentUser);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        var bumped = await verifyDb.Sessions.AsNoTracking().FirstAsync(session => session.Id == sessionId);
        Assert.True(bumped.ExpiresAtUtc > originalExpiresAt, "Sliding TTL should have extended expires_at past the original value.");
    }

    [Fact]
    public async Task CurrentUser_WhenLastSeenWithin24h_DoesNotBumpExpiry()
    {
        var (token, sessionId, originalExpiresAt) = await SignInAsync();
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var graphql = new GraphQLClient(http);

        await graphql.SendAsync<CurrentUserData>("query { currentUser { id } }");

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        var session = await dbContext.Sessions.AsNoTracking().FirstAsync(s => s.Id == sessionId);
        Assert.Equal(originalExpiresAt, session.ExpiresAtUtc, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task CurrentUser_AfterSlidingBumpViaCookie_ReissuesCookieWithFutureExpires()
    {
        var (token, sessionId, _) = await SignInAsync();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
            await dbContext.Sessions
                .Where(session => session.Id == sessionId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(session => session.LastSeenAtUtc, DateTime.UtcNow.AddHours(-25)));
        }

        var http = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/graphql")
        {
            Content = JsonContent.Create(new { query = "query { currentUser { id } }" })
        };
        request.Headers.TryAddWithoutValidation("Cookie", $"buzzkeepr_session={token}");
        request.Headers.TryAddWithoutValidation("Origin", "http://localhost:3000");

        var requestSentAtUtc = DateTime.UtcNow;
        var response = await http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var setCookies), "Sliding bump should re-issue the session cookie.");
        var cookie = setCookies!.Single(c => c.StartsWith("buzzkeepr_session=", StringComparison.Ordinal));
        Assert.Contains($"buzzkeepr_session={token}", cookie);

        var expiresMatch = System.Text.RegularExpressions.Regex.Match(cookie, "expires=([^;]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        Assert.True(expiresMatch.Success, $"Set-Cookie missing expires attribute: {cookie}");
        var newExpires = DateTimeOffset.Parse(expiresMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture).UtcDateTime;

        // The bump should set ExpiresAt to (now + SessionLifetime). SessionLifetime is 30 days; allow a wide window for clock skew + RFC1123 second-truncation.
        var expectedNewExpiry = requestSentAtUtc.AddDays(30);
        Assert.InRange(newExpires, expectedNewExpiry.AddMinutes(-1), expectedNewExpiry.AddMinutes(1));

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        var bumped = await verifyDb.Sessions.AsNoTracking().FirstAsync(session => session.Id == sessionId);
        Assert.InRange(bumped.ExpiresAtUtc, expectedNewExpiry.AddMinutes(-1), expectedNewExpiry.AddMinutes(1));
    }

    [Fact]
    public async Task GraphQL_PostWithCookieButNoOriginOrBearer_Returns403()
    {
        var (token, _, _) = await SignInAsync();

        var http = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/graphql")
        {
            Content = JsonContent.Create(new { query = "query { __typename }" })
        };
        request.Headers.TryAddWithoutValidation("Cookie", $"buzzkeepr_session={token}");

        var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GraphQL_PostWithCookieAndAllowedOrigin_PassesCsrfCheck()
    {
        var (token, _, _) = await SignInAsync();

        var http = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/graphql")
        {
            Content = JsonContent.Create(new { query = "query { currentUser { id } }" })
        };
        request.Headers.TryAddWithoutValidation("Cookie", $"buzzkeepr_session={token}");
        request.Headers.TryAddWithoutValidation("Origin", "http://localhost:3000");

        var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<(string Token, Guid SessionId, DateTime ExpiresAtUtc)> SignInAsync()
    {
        var email = $"session-{Guid.NewGuid():N}@buzzkeepr.test";
        var http = factory.CreateClient();
        var graphql = new GraphQLClient(http);

        await graphql.SendAsync<JsonElement>(
            "mutation($input: RequestEmailSignInInput!) { requestEmailSignIn(input: $input) { success } }",
            new { input = new { email } });
        var code = factory.FakeEmailSender.RequireLatestFor(email).Code;
        var verify = await graphql.SendAsync<VerifyEmailSignInData>(
            "mutation($input: VerifyEmailSignInInput!) { verifyEmailSignIn(input: $input) { user { id } session { token expiresAtUtc } } }",
            new { input = new { email, code } });
        var data = verify.RequireData().VerifyEmailSignIn;

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        var session = await dbContext.Sessions.AsNoTracking().FirstAsync(s => s.UserId == data.User!.Id);

        return (data.Session!.Token, session.Id, session.ExpiresAtUtc);
    }

    private sealed record CurrentUserData(CurrentUser? CurrentUser);
    private sealed record CurrentUser(Guid Id);

    private sealed record VerifyEmailSignInData(VerifyEmailSignInPayload VerifyEmailSignIn);
    private sealed record VerifyEmailSignInPayload(SessionUser? User, SessionEnvelope? Session);
    private sealed record SessionUser(Guid Id);
    private sealed record SessionEnvelope(string Token, DateTime ExpiresAtUtc);
}
