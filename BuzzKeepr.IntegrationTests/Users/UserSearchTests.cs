using System.Text.Json;
using BuzzKeepr.Domain.Entities;
using BuzzKeepr.Domain.Enums;
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
    public async Task SearchUsers_ExcludesPrivateProfiles()
    {
        // ProfileVisibility=Private opts the user out of community discovery — their handle
        // shouldn't appear even on an exact match.
        var (callerToken, _) = await SignInAsync();
        var publicId = await SeedUserAsync(handle: "privtestpub", displayName: null, nickname: null);
        var privateId = await SeedUserAsync(handle: "privtestpriv", displayName: null, nickname: null);
        await SetProfileVisibilityAsync(privateId, ProfileVisibility.Private);

        var graphql = AuthenticatedClient(callerToken);

        var response = await graphql.SendAsync<SearchUsersData>(
            "query { searchUsers(query: \"privtest\", first: 10) { edges { node { id handle } } } }");

        var ids = response.RequireData().SearchUsers.Edges.Select(e => e.Node.Id).ToList();
        Assert.Contains(publicId, ids);
        Assert.DoesNotContain(privateId, ids);
    }

    [Fact]
    public async Task SearchUsers_PublicContact_VisibleToStranger()
    {
        // ContactVisibility=Public means anyone (including strangers) sees the contact fields.
        var (callerToken, _) = await SignInAsync();
        var targetId = await SeedUserAsync(handle: "publicpat", displayName: null, nickname: null);
        await SetContactAsync(targetId, ContactVisibility.Public,
            phone: "+14155552671", instagram: "patinsta");

        var graphql = AuthenticatedClient(callerToken);

        var response = await graphql.SendAsync<SearchUsersContactData>(
            "query { searchUsers(query: \"publicpat\", first: 5) { edges { node { id phoneNumber instagramHandle contactVisibility } } } }");
        var node = response.RequireData().SearchUsers.Edges.Single(e => e.Node.Id == targetId).Node;
        Assert.Equal("+14155552671", node.PhoneNumber);
        Assert.Equal("patinsta", node.InstagramHandle);
        Assert.Equal("PUBLIC", node.ContactVisibility);
    }

    [Fact]
    public async Task SearchUsers_ConnectionsOnlyContact_HiddenFromStranger_VisibleToFriend()
    {
        // ContactVisibility=ConnectionsOnly hides contact from non-friends, exposes to friends.
        var (callerToken, callerId) = await SignInAsync();
        var (_, friendToken, friendId) = await SignInAsync2();
        var targetId = await SeedUserAsync(handle: "convocon", displayName: null, nickname: null);
        await SetContactAsync(targetId, ContactVisibility.ConnectionsOnly,
            phone: "+14155551111", instagram: "coninsta");

        // Stranger view — caller is not friends with target.
        var strangerResponse = await AuthenticatedClient(callerToken).SendAsync<SearchUsersContactData>(
            "query { searchUsers(query: \"convocon\", first: 5) { edges { node { id phoneNumber instagramHandle contactVisibility } } } }");
        var strangerNode = strangerResponse.RequireData().SearchUsers.Edges.Single(e => e.Node.Id == targetId).Node;
        Assert.Null(strangerNode.PhoneNumber);
        Assert.Null(strangerNode.InstagramHandle);
        Assert.Equal("CONNECTIONS_ONLY", strangerNode.ContactVisibility);

        // Friend view — establish friendship between friendId and target, then search as friend.
        await SeedAcceptedFriendshipAsync(friendId, targetId);
        var friendResponse = await AuthenticatedClient(friendToken).SendAsync<SearchUsersContactData>(
            "query { searchUsers(query: \"convocon\", first: 5) { edges { node { id phoneNumber instagramHandle contactVisibility } } } }");
        var friendNode = friendResponse.RequireData().SearchUsers.Edges.Single(e => e.Node.Id == targetId).Node;
        Assert.Equal("+14155551111", friendNode.PhoneNumber);
        Assert.Equal("coninsta", friendNode.InstagramHandle);
    }

    [Fact]
    public async Task SearchUsers_PrivateContact_HiddenFromEveryone()
    {
        // Even friends shouldn't see contact when visibility is Private — strict gating per
        // the visibility model. Verifies the "Private always hides" rule end-to-end.
        var (callerToken, callerId) = await SignInAsync();
        var targetId = await SeedUserAsync(handle: "privpriv", displayName: null, nickname: null);
        await SetContactAsync(targetId, ContactVisibility.Private,
            phone: "+14155559999", instagram: "privinsta");
        await SeedAcceptedFriendshipAsync(callerId, targetId);

        var response = await AuthenticatedClient(callerToken).SendAsync<SearchUsersContactData>(
            "query { searchUsers(query: \"privpriv\", first: 5) { edges { node { id phoneNumber instagramHandle contactVisibility } } } }");
        var node = response.RequireData().SearchUsers.Edges.Single(e => e.Node.Id == targetId).Node;
        Assert.Null(node.PhoneNumber);
        Assert.Null(node.InstagramHandle);
        Assert.Equal("PRIVATE", node.ContactVisibility);
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
            EmailVerified = true,
            CreatedAtUtc = DateTime.UtcNow,
        };
        if (handle != null || displayName != null || nickname != null)
        {
            var profile = user.EnsureProfile();
            profile.Handle = handle;
            profile.DisplayName = displayName;
            profile.Nickname = nickname;
        }
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        return user.Id;
    }

    private async Task SetUserHandleAsync(Guid userId, string handle)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        var profile = await dbContext.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
        if (profile is null)
        {
            profile = new UserProfile
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CreatedAtUtc = DateTime.UtcNow
            };
            dbContext.UserProfiles.Add(profile);
        }
        profile.Handle = handle;
        await dbContext.SaveChangesAsync();
    }

    private async Task SetProfileVisibilityAsync(Guid userId, ProfileVisibility visibility)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        var profile = await dbContext.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
        if (profile is null)
        {
            profile = new UserProfile
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CreatedAtUtc = DateTime.UtcNow
            };
            dbContext.UserProfiles.Add(profile);
        }
        profile.ProfileVisibility = visibility;
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

    // Convenience for tests that need a second authenticated user. Returns (email, token, userId).
    private async Task<(string Email, string Token, Guid UserId)> SignInAsync2()
    {
        var email = $"search2-{Guid.NewGuid():N}@buzzkeepr.test";
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
        return (email, data.Session!.Token, data.User!.Id);
    }

    private async Task SetContactAsync(Guid userId, ContactVisibility visibility, string? phone = null, string? instagram = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        var profile = await dbContext.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
        if (profile is null)
        {
            profile = new UserProfile
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CreatedAtUtc = DateTime.UtcNow
            };
            dbContext.UserProfiles.Add(profile);
        }
        profile.ContactVisibility = visibility;
        if (phone is not null) profile.PhoneNumber = phone;
        if (instagram is not null) profile.InstagramHandle = instagram;
        await dbContext.SaveChangesAsync();
    }

    private async Task SeedAcceptedFriendshipAsync(Guid userA, Guid userB)
    {
        // Direct DB insert avoids running through the GraphQL flow — these tests don't care
        // about the request/accept dance, just that the friendship row exists.
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        dbContext.Friendships.Add(new Friendship
        {
            Id = Guid.NewGuid(),
            RequesterId = userA,
            AddresseeId = userB,
            Status = FriendshipStatus.Accepted,
            CreatedAtUtc = DateTime.UtcNow,
            RespondedAtUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();
    }

    private sealed record SearchUsersData(SearchUsersConnection SearchUsers);
    private sealed record SearchUsersConnection(List<SearchUsersEdge> Edges, SearchUsersPageInfo PageInfo);
    private sealed record SearchUsersEdge(SearchUsersNode Node, string? Cursor);
    private sealed record SearchUsersNode(Guid Id, string? Handle, string? Nickname, string? DisplayName);
    private sealed record SearchUsersPageInfo(bool HasNextPage, string? EndCursor);

    private sealed record SearchUsersContactData(SearchUsersContactConnection SearchUsers);
    private sealed record SearchUsersContactConnection(List<SearchUsersContactEdge> Edges);
    private sealed record SearchUsersContactEdge(SearchUsersContactNode Node);
    private sealed record SearchUsersContactNode(Guid Id, string? PhoneNumber, string? InstagramHandle, string ContactVisibility);

    private sealed record VerifyData(VerifyPayload VerifyEmailSignIn);
    private sealed record VerifyPayload(VerifyUser? User, VerifySession? Session);
    private sealed record VerifyUser(Guid Id);
    private sealed record VerifySession(string Token);
}
