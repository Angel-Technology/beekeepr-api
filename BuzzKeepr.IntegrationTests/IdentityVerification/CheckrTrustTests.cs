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
    public async Task StartInstantCriminalCheck_RequiresPersonaVerifiedIdentity()
    {
        var (token, _) = await SignInAsync();
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var graphql = new GraphQLClient(http);

        var response = await graphql.SendAsync<StartCheckData>(
            "mutation { startInstantCriminalCheck(input: {}) { success error } }");

        var payload = response.RequireData().StartInstantCriminalCheck;
        Assert.False(payload.Success);
        Assert.Equal("Identity verification must be completed before running a background check.", payload.Error);
        Assert.Empty(factory.FakeCheckrTrust.Calls);
    }

    [Fact]
    public async Task StartInstantCriminalCheck_FirstRun_PullsFromVerifiedIdentityAndPersistsProfileId()
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
        await SeedVerifiedIdentityAsync(userId, firstName: "Jane", middleName: "Quinn", lastName: "Smith", birthdate: "19900101", licenseState: "CA");

        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var graphql = new GraphQLClient(http);

        var response = await graphql.SendAsync<StartCheckData>(
            "mutation { startInstantCriminalCheck(input: {}) { success checkId profileId resultCount hasPossibleMatches error } }");

        var payload = response.RequireData().StartInstantCriminalCheck;
        Assert.True(payload.Success);
        Assert.Equal("chk_first", payload.CheckId);
        Assert.Equal("prf_first", payload.ProfileId);

        var call = Assert.Single(factory.FakeCheckrTrust.Calls);
        Assert.Null(call.ProfileId);
        Assert.Equal("Jane", call.FirstName);
        Assert.Equal("Quinn", call.MiddleName);
        Assert.Equal("Smith", call.LastName);
        Assert.Equal("19900101", call.Birthdate);
        Assert.Equal("CA", call.State);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        var user = await dbContext.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
        Assert.Equal("prf_first", user.CheckrProfileId);
        Assert.Equal("chk_first", user.CheckrLastCheckId);
    }

    [Fact]
    public async Task StartInstantCriminalCheck_WithPhoneInput_PersistsPhoneOnUser()
    {
        factory.FakeCheckrTrust.NextResult = new CreateInstantCriminalCheckResult
        {
            Success = true,
            CheckId = "chk_phone",
            ProfileId = "prf_phone"
        };

        var (token, userId) = await SignInAsync();
        await SeedVerifiedIdentityAsync(userId);

        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var graphql = new GraphQLClient(http);

        var response = await graphql.SendAsync<StartCheckData>(
            "mutation($input: StartInstantCriminalCheckInput!) { startInstantCriminalCheck(input: $input) { success error } }",
            new { input = new { phoneNumber = "+14155552671" } });

        Assert.True(response.RequireData().StartInstantCriminalCheck.Success);

        var call = Assert.Single(factory.FakeCheckrTrust.Calls);
        Assert.Equal("+14155552671", call.PhoneNumber);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        var user = await dbContext.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
        Assert.Equal("+14155552671", user.PhoneNumber);
    }

    [Fact]
    public async Task StartInstantCriminalCheck_SecondRun_ReusesProfileIdAndSkipsPii()
    {
        factory.FakeCheckrTrust.NextResult = new CreateInstantCriminalCheckResult
        {
            Success = true,
            CheckId = "chk_initial",
            ProfileId = "prf_persisted"
        };

        var (token, userId) = await SignInAsync();
        await SeedVerifiedIdentityAsync(userId);

        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var graphql = new GraphQLClient(http);

        await graphql.SendAsync<StartCheckData>(
            "mutation { startInstantCriminalCheck(input: {}) { success } }");

        factory.FakeCheckrTrust.NextResult = new CreateInstantCriminalCheckResult
        {
            Success = true,
            CheckId = "chk_rerun",
            ProfileId = "prf_persisted",
            ResultCount = 1,
            HasPossibleMatches = true
        };

        var rerunResponse = await graphql.SendAsync<StartCheckData>(
            "mutation { startInstantCriminalCheck(input: {}) { success checkId hasPossibleMatches error } }");

        Assert.True(rerunResponse.RequireData().StartInstantCriminalCheck.Success);
        Assert.Equal(2, factory.FakeCheckrTrust.Calls.Count);
        var rerunCall = factory.FakeCheckrTrust.Calls[1];
        Assert.Equal("prf_persisted", rerunCall.ProfileId);
        Assert.True(string.IsNullOrEmpty(rerunCall.FirstName));
    }

    private async Task SeedVerifiedIdentityAsync(
        Guid userId,
        string firstName = "Test",
        string? middleName = null,
        string lastName = "User",
        string birthdate = "19900101",
        string licenseState = "CA")
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        await dbContext.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(u => u.VerifiedFirstName, firstName)
                .SetProperty(u => u.VerifiedMiddleName, middleName)
                .SetProperty(u => u.VerifiedLastName, lastName)
                .SetProperty(u => u.VerifiedBirthdate, birthdate)
                .SetProperty(u => u.VerifiedLicenseState, licenseState));
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
