using System.Text.Json;
using BuzzKeepr.Application.IdentityVerification.Models;
using BuzzKeepr.Infrastructure.Persistence;
using BuzzKeepr.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BuzzKeepr.IntegrationTests.IdentityVerification;

[Collection(IntegrationTestCollection.Name)]
public sealed class CheckrTrustTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly BuzzKeeprApiFactory factory = new(postgres);

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync()
    {
        factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task StartInstantCriminalCheck_FirstRun_SendsPiiAndPersistsProfileId()
    {
        factory.FakeCheckrTrust.NextResult = new CreateInstantCriminalCheckResult
        {
            Success = true,
            CheckId = "chk_first",
            ProfileId = "prf_first",
            ResultCount = 0,
            HasPossibleMatches = false
        };

        var (token, userId) = await SignInAsync();
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var graphql = new GraphQLClient(http);

        var response = await graphql.SendAsync<StartCheckData>(
            "mutation($input: StartInstantCriminalCheckInput!) { startInstantCriminalCheck(input: $input) { success checkId profileId resultCount hasPossibleMatches error } }",
            new { input = new { firstName = "Jane", lastName = "Smith", dateOfBirth = "19900101" } });

        var payload = response.RequireData().StartInstantCriminalCheck;
        Assert.True(payload.Success);
        Assert.Equal("chk_first", payload.CheckId);
        Assert.Equal("prf_first", payload.ProfileId);
        Assert.Equal(0, payload.ResultCount);
        Assert.False(payload.HasPossibleMatches);

        var call = Assert.Single(factory.FakeCheckrTrust.Calls);
        Assert.Null(call.ProfileId);
        Assert.Equal("Jane", call.FirstName);
        Assert.Equal("Smith", call.LastName);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        var user = await dbContext.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
        Assert.Equal("prf_first", user.CheckrProfileId);
        Assert.Equal("chk_first", user.CheckrLastCheckId);
        Assert.False(user.CheckrLastCheckHasPossibleMatches);
        Assert.NotNull(user.CheckrLastCheckAtUtc);
    }

    [Fact]
    public async Task StartInstantCriminalCheck_SecondRun_ReusesProfileIdAndSkipsPii()
    {
        factory.FakeCheckrTrust.NextResult = new CreateInstantCriminalCheckResult
        {
            Success = true,
            CheckId = "chk_initial",
            ProfileId = "prf_persisted",
            ResultCount = 0,
            HasPossibleMatches = false
        };

        var (token, _) = await SignInAsync();
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var graphql = new GraphQLClient(http);

        await graphql.SendAsync<StartCheckData>(
            "mutation($input: StartInstantCriminalCheckInput!) { startInstantCriminalCheck(input: $input) { success } }",
            new { input = new { firstName = "Jane", lastName = "Smith", dateOfBirth = "19900101" } });

        factory.FakeCheckrTrust.NextResult = new CreateInstantCriminalCheckResult
        {
            Success = true,
            CheckId = "chk_rerun",
            ProfileId = "prf_persisted",
            ResultCount = 1,
            HasPossibleMatches = true
        };

        var rerunResponse = await graphql.SendAsync<StartCheckData>(
            "mutation { startInstantCriminalCheck(input: {firstName: \"\", lastName: \"\"}) { success checkId hasPossibleMatches error } }");

        var rerun = rerunResponse.RequireData().StartInstantCriminalCheck;
        Assert.True(rerun.Success, rerun.Error);
        Assert.Equal("chk_rerun", rerun.CheckId);
        Assert.True(rerun.HasPossibleMatches);

        Assert.Equal(2, factory.FakeCheckrTrust.Calls.Count);
        var rerunCall = factory.FakeCheckrTrust.Calls[1];
        Assert.Equal("prf_persisted", rerunCall.ProfileId);
        Assert.True(string.IsNullOrEmpty(rerunCall.FirstName));
        Assert.True(string.IsNullOrEmpty(rerunCall.LastName));
    }

    private async Task<(string Token, Guid UserId)> SignInAsync()
    {
        var email = $"checkr-{Guid.NewGuid():N}@buzzkeepr.test";
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

    private sealed record StartCheckData(StartCheckPayload StartInstantCriminalCheck);
    private sealed record StartCheckPayload(bool Success, string? CheckId, string? ProfileId, int? ResultCount, bool? HasPossibleMatches, string? Error);

    private sealed record VerifyData(VerifyPayload VerifyEmailSignIn);
    private sealed record VerifyPayload(VerifyUser? User, VerifySession? Session);
    private sealed record VerifyUser(Guid Id);
    private sealed record VerifySession(string Token);
}
