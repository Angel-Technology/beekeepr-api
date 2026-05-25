using System.Text.Json;
using BuzzKeepr.Domain.Entities;
using BuzzKeepr.Infrastructure.Persistence;
using BuzzKeepr.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BuzzKeepr.IntegrationTests.Users;

[Collection(IntegrationTestCollection.Name)]
public sealed class HandleAvailabilityTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly BuzzKeeprApiFactory factory = new(postgres);

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync()
    {
        factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CheckHandleAvailability_UnusedHandleIsAvailable()
    {
        var (token, _) = await SignInAsync();
        var graphql = AuthenticatedClient(token);

        var response = await Query(graphql, "freshhandle");

        Assert.True(response.Available);
        Assert.Null(response.Reason);
    }

    [Theory]
    [InlineData("ab", "too_short")]
    [InlineData("", "too_short")]
    [InlineData("   ", "too_short")]
    public async Task CheckHandleAvailability_ShortHandleReturnsTooShort(string handle, string expectedReason)
    {
        var (token, _) = await SignInAsync();
        var graphql = AuthenticatedClient(token);

        var response = await Query(graphql, handle);

        Assert.False(response.Available);
        Assert.Equal(expectedReason, response.Reason);
    }

    [Fact]
    public async Task CheckHandleAvailability_LongHandleReturnsTooLong()
    {
        var (token, _) = await SignInAsync();
        var graphql = AuthenticatedClient(token);

        // 21 chars exceeds the 20-char limit.
        var response = await Query(graphql, new string('a', 21));

        Assert.False(response.Available);
        Assert.Equal("too_long", response.Reason);
    }

    [Theory]
    [InlineData("bad-handle")]
    [InlineData("with space")]
    [InlineData("emoji😀foo")]
    [InlineData("hash#tag")]
    public async Task CheckHandleAvailability_InvalidCharsReturnsInvalidFormat(string handle)
    {
        var (token, _) = await SignInAsync();
        var graphql = AuthenticatedClient(token);

        var response = await Query(graphql, handle);

        Assert.False(response.Available);
        Assert.Equal("invalid_format", response.Reason);
    }

    [Fact]
    public async Task CheckHandleAvailability_HandleTakenByAnotherUserReturnsTaken()
    {
        var (token, _) = await SignInAsync();
        await SeedUserAsync(handle: "claimed");

        var graphql = AuthenticatedClient(token);

        var response = await Query(graphql, "claimed");

        Assert.False(response.Available);
        Assert.Equal("taken", response.Reason);
    }

    [Fact]
    public async Task CheckHandleAvailability_CurrentUsersOwnHandleIsAvailable()
    {
        var (token, userId) = await SignInAsync();
        await SetUserHandleAsync(userId, "myhandle");

        var graphql = AuthenticatedClient(token);

        var response = await Query(graphql, "myhandle");

        // The caller already owns this handle — re-saving it shouldn't be blocked as "taken".
        Assert.True(response.Available);
        Assert.Null(response.Reason);
    }

    [Fact]
    public async Task CheckHandleAvailability_IsCaseInsensitive()
    {
        var (token, _) = await SignInAsync();
        await SeedUserAsync(handle: "mixedcase");

        var graphql = AuthenticatedClient(token);

        var response = await Query(graphql, "MixedCase");

        Assert.False(response.Available);
        Assert.Equal("taken", response.Reason);
    }

    [Fact]
    public async Task CheckHandleAvailability_WithoutSessionReturnsAuthenticationRequired()
    {
        var graphql = new GraphQLClient(factory.CreateClient());

        var response = await Query(graphql, "anything");

        Assert.False(response.Available);
        Assert.Equal("authentication_required", response.Reason);
    }

    private static async Task<HandleAvailabilityNode> Query(GraphQLClient graphql, string handle)
    {
        var response = await graphql.SendAsync<CheckHandleAvailabilityData>(
            "query($h: String!) { checkHandleAvailability(handle: $h) { available reason } }",
            new { h = handle });
        return response.RequireData().CheckHandleAvailability;
    }

    private GraphQLClient AuthenticatedClient(string token)
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return new GraphQLClient(http);
    }

    private async Task SeedUserAsync(string handle)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        dbContext.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Email = $"seed-{Guid.NewGuid():N}@buzzkeepr.test",
            Handle = handle,
            EmailVerified = true,
            CreatedAtUtc = DateTime.UtcNow,
        });
        await dbContext.SaveChangesAsync();
    }

    private async Task SetUserHandleAsync(Guid userId, string handle)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        var user = await dbContext.Users.FirstAsync(u => u.Id == userId);
        user.Handle = handle;
        await dbContext.SaveChangesAsync();
    }

    private async Task<(string Token, Guid UserId)> SignInAsync()
    {
        var email = $"handle-{Guid.NewGuid():N}@buzzkeepr.test";
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

    private sealed record CheckHandleAvailabilityData(HandleAvailabilityNode CheckHandleAvailability);
    private sealed record HandleAvailabilityNode(bool Available, string? Reason);

    private sealed record VerifyData(VerifyPayload VerifyEmailSignIn);
    private sealed record VerifyPayload(VerifyUser? User, VerifySession? Session);
    private sealed record VerifyUser(Guid Id);
    private sealed record VerifySession(string Token);
}
