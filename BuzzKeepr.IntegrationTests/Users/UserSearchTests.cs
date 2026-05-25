using System.Text.Json;
using BuzzKeepr.Domain.Entities;
using BuzzKeepr.Infrastructure.Persistence;
using BuzzKeepr.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BuzzKeepr.IntegrationTests.Users;

[Collection(IntegrationTestCollection.Name)]
public sealed class UserSearchTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly BuzzKeeprApiFactory factory = new(postgres);

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync()
    {
        factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SearchUsers_RanksExactHandleAbovePrefixAboveFuzzy()
    {
        var (callerToken, _) = await SignInAsync();
        await SeedUserAsync(handle: "samuel", displayName: "Different Person", nickname: null);
        await SeedUserAsync(handle: "samuelfoo", displayName: "Another", nickname: null);
        await SeedUserAsync(handle: null, displayName: "Samuel Wemimo", nickname: "samul"); // fuzzy

        var graphql = AuthenticatedClient(callerToken);

        var response = await graphql.SendAsync<SearchUsersData>(
            "query($q: String!) { searchUsers(query: $q, first: 10) { edges { node { handle nickname displayName } } } }",
            new { q = "samuel" });

        var nodes = response.RequireData().SearchUsers.Edges.Select(e => e.Node).ToList();
        // Exact handle match first, then handle prefix match, then fuzzy nickname/displayName.
        Assert.Equal("samuel", nodes[0].Handle);
        Assert.Equal("samuelfoo", nodes[1].Handle);
        Assert.Contains(nodes, n => n.Nickname == "samul");
    }

    [Fact]
    public async Task SearchUsers_ExcludesCurrentUser()
    {
        var (callerToken, callerId) = await SignInAsync();
        // Sign-in flow creates the caller with a generated handle/nickname; force them into
        // the result set's query window so the only reason they'd be missing is the exclusion.
        await SetUserHandleAsync(callerId, "needle");
        await SeedUserAsync(handle: "needle2", displayName: null, nickname: null);

        var graphql = AuthenticatedClient(callerToken);

        var response = await graphql.SendAsync<SearchUsersData>(
            "query { searchUsers(query: \"need\", first: 10) { edges { node { handle } } } }");

        var handles = response.RequireData().SearchUsers.Edges.Select(e => e.Node.Handle).ToList();
        Assert.DoesNotContain("needle", handles);
        Assert.Contains("needle2", handles);
    }

    [Fact]
    public async Task SearchUsers_ShortQueryReturnsEmpty()
    {
        var (callerToken, _) = await SignInAsync();
        await SeedUserAsync(handle: "ari", displayName: null, nickname: null);
        await SeedUserAsync(handle: "amy", displayName: null, nickname: null);

        var graphql = AuthenticatedClient(callerToken);

        var response = await graphql.SendAsync<SearchUsersData>(
            "query { searchUsers(query: \"a\", first: 10) { edges { node { handle } } } }");

        Assert.Empty(response.RequireData().SearchUsers.Edges);
    }

    [Fact]
    public async Task SearchUsers_PaginationReturnsRequestedSliceWithCursor()
    {
        var (callerToken, _) = await SignInAsync();
        for (var i = 0; i < 5; i++)
        {
            await SeedUserAsync(handle: $"paged{i}", displayName: null, nickname: null);
        }

        var graphql = AuthenticatedClient(callerToken);

        var first = await graphql.SendAsync<SearchUsersData>(
            "query { searchUsers(query: \"paged\", first: 2) { edges { node { handle } cursor } pageInfo { hasNextPage endCursor } } }");
        var firstPage = first.RequireData().SearchUsers;
        Assert.Equal(2, firstPage.Edges.Count);
        Assert.True(firstPage.PageInfo.HasNextPage);
        Assert.NotNull(firstPage.PageInfo.EndCursor);

        var second = await graphql.SendAsync<SearchUsersData>(
            "query($after: String!) { searchUsers(query: \"paged\", first: 2, after: $after) { edges { node { handle } } pageInfo { hasNextPage } } }",
            new { after = firstPage.PageInfo.EndCursor });
        var secondPage = second.RequireData().SearchUsers;
        Assert.Equal(2, secondPage.Edges.Count);

        // Pages must not overlap.
        var firstHandles = firstPage.Edges.Select(e => e.Node.Handle).ToHashSet();
        Assert.All(secondPage.Edges, edge => Assert.DoesNotContain(edge.Node.Handle, firstHandles));
    }

    [Fact]
    public async Task SearchUsers_SoftDeletedUsersAreExcluded()
    {
        var (callerToken, _) = await SignInAsync();
        var visibleId = await SeedUserAsync(handle: "ghost1", displayName: null, nickname: null);
        var hiddenId = await SeedUserAsync(handle: "ghost2", displayName: null, nickname: null);
        await SoftDeleteAsync(hiddenId);

        var graphql = AuthenticatedClient(callerToken);

        var response = await graphql.SendAsync<SearchUsersData>(
            "query { searchUsers(query: \"ghost\", first: 10) { edges { node { id handle } } } }");

        var ids = response.RequireData().SearchUsers.Edges.Select(e => e.Node.Id).ToList();
        Assert.Contains(visibleId, ids);
        Assert.DoesNotContain(hiddenId, ids);
    }

    [Fact]
    public async Task SearchUsers_WithoutSessionReturnsEmpty()
    {
        await SeedUserAsync(handle: "anyone", displayName: null, nickname: null);
        var graphql = new GraphQLClient(factory.CreateClient());

        var response = await graphql.SendAsync<SearchUsersData>(
            "query { searchUsers(query: \"any\", first: 10) { edges { node { handle } } } }");

        Assert.Empty(response.RequireData().SearchUsers.Edges);
    }

    private GraphQLClient AuthenticatedClient(string token)
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return new GraphQLClient(http);
    }

    private async Task<Guid> SeedUserAsync(string? handle, string? displayName, string? nickname)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"seed-{Guid.NewGuid():N}@buzzkeepr.test",
            Handle = handle,
            DisplayName = displayName,
            Nickname = nickname,
            EmailVerified = true,
            CreatedAtUtc = DateTime.UtcNow,
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        return user.Id;
    }

    private async Task SetUserHandleAsync(Guid userId, string handle)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        var user = await dbContext.Users.FirstAsync(u => u.Id == userId);
        user.Handle = handle;
        await dbContext.SaveChangesAsync();
    }

    private async Task SoftDeleteAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        var user = await dbContext.Users.FirstAsync(u => u.Id == userId);
        user.DeletedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();
    }

    private async Task<(string Token, Guid UserId)> SignInAsync()
    {
        var email = $"search-{Guid.NewGuid():N}@buzzkeepr.test";
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

    private sealed record SearchUsersData(SearchUsersConnection SearchUsers);
    private sealed record SearchUsersConnection(List<SearchUsersEdge> Edges, SearchUsersPageInfo PageInfo);
    private sealed record SearchUsersEdge(SearchUsersNode Node, string? Cursor);
    private sealed record SearchUsersNode(Guid Id, string? Handle, string? Nickname, string? DisplayName);
    private sealed record SearchUsersPageInfo(bool HasNextPage, string? EndCursor);

    private sealed record VerifyData(VerifyPayload VerifyEmailSignIn);
    private sealed record VerifyPayload(VerifyUser? User, VerifySession? Session);
    private sealed record VerifyUser(Guid Id);
    private sealed record VerifySession(string Token);
}
