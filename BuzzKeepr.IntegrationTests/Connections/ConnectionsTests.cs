using System.Text.Json;
using BuzzKeepr.Domain.Entities;
using BuzzKeepr.Infrastructure.Persistence;
using BuzzKeepr.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BuzzKeepr.IntegrationTests.Connections;

[Collection(IntegrationTestCollection.Name)]
public sealed class ConnectionsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly BuzzKeeprApiFactory factory = new(postgres);

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync()
    {
        factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SendFriendRequest_AppearsInBothPendingLists()
    {
        var (callerToken, callerId) = await SignInAsync();
        var (targetToken, targetId) = await SignInAsync();

        await SendFriendRequestAsync(callerToken, targetId);

        var outgoing = await ListPendingAsync(callerToken, "outgoingFriendRequests");
        var incoming = await ListPendingAsync(targetToken, "incomingFriendRequests");

        Assert.Contains(targetId, outgoing);
        Assert.Contains(callerId, incoming);
    }

    [Fact]
    public async Task AcceptFriendRequest_PutsBothInFriendsList()
    {
        var (callerToken, callerId) = await SignInAsync();
        var (targetToken, targetId) = await SignInAsync();

        await SendFriendRequestAsync(callerToken, targetId);
        await AcceptFriendRequestAsync(targetToken, callerId);

        var callerFriends = await ListPendingAsync(callerToken, "friends");
        var targetFriends = await ListPendingAsync(targetToken, "friends");

        Assert.Contains(targetId, callerFriends);
        Assert.Contains(callerId, targetFriends);
    }

    [Fact]
    public async Task SendFriendRequest_ReversePending_AutoAccepts()
    {
        var (aliceToken, aliceId) = await SignInAsync();
        var (bobToken, bobId) = await SignInAsync();

        // Alice → Bob, then Bob → Alice. The second call should auto-accept since both sides
        // expressed interest, not create a duplicate row.
        await SendFriendRequestAsync(aliceToken, bobId);
        await SendFriendRequestAsync(bobToken, aliceId);

        var aliceFriends = await ListPendingAsync(aliceToken, "friends");
        var bobFriends = await ListPendingAsync(bobToken, "friends");

        Assert.Contains(bobId, aliceFriends);
        Assert.Contains(aliceId, bobFriends);
    }

    [Fact]
    public async Task DeclineFriendRequest_RemovesPendingFromBothSides()
    {
        var (callerToken, callerId) = await SignInAsync();
        var (targetToken, targetId) = await SignInAsync();

        await SendFriendRequestAsync(callerToken, targetId);

        var declineResponse = await AuthenticatedClient(targetToken).SendAsync<DeclineData>(
            "mutation($input: RespondToFriendRequestInput!) { declineFriendRequest(input: $input) { success error } }",
            new { input = new { otherUserId = callerId } });
        Assert.True(declineResponse.RequireData().DeclineFriendRequest.Success);

        var outgoing = await ListPendingAsync(callerToken, "outgoingFriendRequests");
        var incoming = await ListPendingAsync(targetToken, "incomingFriendRequests");

        Assert.Empty(outgoing);
        Assert.Empty(incoming);
    }

    [Fact]
    public async Task CancelFriendRequest_RemovesOutgoingPending()
    {
        var (callerToken, _) = await SignInAsync();
        var (_, targetId) = await SignInAsync();

        await SendFriendRequestAsync(callerToken, targetId);

        var cancelResponse = await AuthenticatedClient(callerToken).SendAsync<CancelData>(
            "mutation($input: RespondToFriendRequestInput!) { cancelFriendRequest(input: $input) { success error } }",
            new { input = new { otherUserId = targetId } });
        Assert.True(cancelResponse.RequireData().CancelFriendRequest.Success);

        var outgoing = await ListPendingAsync(callerToken, "outgoingFriendRequests");
        Assert.Empty(outgoing);
    }

    [Fact]
    public async Task RemoveFriend_AfterAccept_ClearsBothFriendLists()
    {
        var (callerToken, callerId) = await SignInAsync();
        var (targetToken, targetId) = await SignInAsync();

        await SendFriendRequestAsync(callerToken, targetId);
        await AcceptFriendRequestAsync(targetToken, callerId);

        var removeResponse = await AuthenticatedClient(callerToken).SendAsync<RemoveFriendData>(
            "mutation($input: RemoveFriendInput!) { removeFriend(input: $input) { success error } }",
            new { input = new { otherUserId = targetId } });
        Assert.True(removeResponse.RequireData().RemoveFriend.Success);

        var callerFriends = await ListPendingAsync(callerToken, "friends");
        var targetFriends = await ListPendingAsync(targetToken, "friends");

        Assert.Empty(callerFriends);
        Assert.Empty(targetFriends);
    }

    [Fact]
    public async Task BlockUser_RemovesFriendshipAndHidesFromSearch()
    {
        var (callerToken, _) = await SignInAsync();
        var (targetToken, targetId) = await SignInAsync();
        await SetUserHandleAsync(targetId, "blocktarget");

        await SendFriendRequestAsync(callerToken, targetId);
        await AcceptFriendRequestAsync(targetToken, GetUserIdFromToken(callerToken));

        var blockResponse = await AuthenticatedClient(callerToken).SendAsync<BlockData>(
            "mutation($input: BlockUserInput!) { blockUser(input: $input) { success error } }",
            new { input = new { targetUserId = targetId } });
        Assert.True(blockResponse.RequireData().BlockUser.Success);

        var friends = await ListPendingAsync(callerToken, "friends");
        var blocked = await ListPendingAsync(callerToken, "blockedUsers");
        var searchResults = await SearchAsync(callerToken, "blocktarget");

        Assert.Empty(friends);
        Assert.Contains(targetId, blocked);
        Assert.DoesNotContain(targetId, searchResults.Select(r => r.Id));
    }

    [Fact]
    public async Task BlockUser_TargetCantFindBlockerInSearchEither()
    {
        var (callerToken, callerId) = await SignInAsync();
        var (targetToken, targetId) = await SignInAsync();
        await SetUserHandleAsync(callerId, "blockerhandle");

        await BlockAsync(callerToken, targetId);

        // Block is asymmetric for the relationship itself, but search visibility is symmetric:
        // neither side should be able to discover/contact the other through search.
        var blockerFromTargetView = await SearchAsync(targetToken, "blockerhandle");
        Assert.DoesNotContain(callerId, blockerFromTargetView.Select(r => r.Id));
    }

    [Fact]
    public async Task UnblockUser_RestoresSearchVisibility()
    {
        var (callerToken, _) = await SignInAsync();
        var (_, targetId) = await SignInAsync();
        await SetUserHandleAsync(targetId, "unblocktarget");

        await BlockAsync(callerToken, targetId);
        var unblockResponse = await AuthenticatedClient(callerToken).SendAsync<UnblockData>(
            "mutation($input: UnblockUserInput!) { unblockUser(input: $input) { success error } }",
            new { input = new { targetUserId = targetId } });
        Assert.True(unblockResponse.RequireData().UnblockUser.Success);

        var searchResults = await SearchAsync(callerToken, "unblocktarget");
        Assert.Contains(targetId, searchResults.Select(r => r.Id));
    }

    [Fact]
    public async Task FlagUser_RemovesFriendshipCreatesBlockAndPersistsFlagRow()
    {
        var (callerToken, callerId) = await SignInAsync();
        var (targetToken, targetId) = await SignInAsync();

        await SendFriendRequestAsync(callerToken, targetId);
        await AcceptFriendRequestAsync(targetToken, callerId);

        var flagResponse = await AuthenticatedClient(callerToken).SendAsync<FlagData>(
            "mutation($input: FlagUserInput!) { flagUser(input: $input) { success error } }",
            new { input = new { targetUserId = targetId } });
        Assert.True(flagResponse.RequireData().FlagUser.Success);

        var friends = await ListPendingAsync(callerToken, "friends");
        var blocked = await ListPendingAsync(callerToken, "blockedUsers");

        Assert.Empty(friends);
        Assert.Contains(targetId, blocked);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        Assert.True(await dbContext.UserFlags.AnyAsync(f => f.FlaggerId == callerId && f.FlaggedUserId == targetId));
    }

    [Fact]
    public async Task FlagUser_Idempotent()
    {
        var (callerToken, callerId) = await SignInAsync();
        var (_, targetId) = await SignInAsync();

        await FlagAsync(callerToken, targetId);
        await FlagAsync(callerToken, targetId);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        // Same flagger → same target counts once; the (FlaggerId, FlaggedUserId) unique index
        // turns re-flag into a no-op.
        var flagCount = await dbContext.UserFlags
            .CountAsync(f => f.FlaggerId == callerId && f.FlaggedUserId == targetId);
        Assert.Equal(1, flagCount);
    }

    [Fact]
    public async Task SendFriendRequest_ToBlockedByTarget_ReturnsGenericError()
    {
        var (callerToken, callerId) = await SignInAsync();
        var (targetToken, targetId) = await SignInAsync();

        await BlockAsync(targetToken, callerId);

        var response = await AuthenticatedClient(callerToken).SendAsync<SendRequestData>(
            "mutation($input: SendFriendRequestInput!) { sendFriendRequest(input: $input) { friendship { id } error } }",
            new { input = new { targetUserId = targetId } });
        var payload = response.RequireData().SendFriendRequest;

        // Must not leak "you are blocked" — same generic copy as target-not-found.
        Assert.Equal("Unable to send friend request.", payload.Error);
        Assert.Null(payload.Friendship);
    }

    [Fact]
    public async Task SendFriendRequest_ToSelf_ReturnsSelfTargetError()
    {
        var (callerToken, callerId) = await SignInAsync();

        var response = await AuthenticatedClient(callerToken).SendAsync<SendRequestData>(
            "mutation($input: SendFriendRequestInput!) { sendFriendRequest(input: $input) { friendship { id } error } }",
            new { input = new { targetUserId = callerId } });

        Assert.Contains("yourself", response.RequireData().SendFriendRequest.Error);
    }

    [Fact]
    public async Task SearchUsers_AnnotatesViewerFriendshipState()
    {
        var (callerToken, _) = await SignInAsync();
        var (friendToken, friendId) = await SignInAsync();
        var (_, pendingOutId) = await SignInAsync();
        var (pendingInToken, pendingInId) = await SignInAsync();
        var (_, strangerId) = await SignInAsync();

        await SetUserHandleAsync(friendId, "anneta");
        await SetUserHandleAsync(pendingOutId, "annetb");
        await SetUserHandleAsync(pendingInId, "annetc");
        await SetUserHandleAsync(strangerId, "annetd");

        // Friend
        await SendFriendRequestAsync(callerToken, friendId);
        await AcceptFriendRequestAsync(friendToken, GetUserIdFromToken(callerToken));
        // Outgoing pending
        await SendFriendRequestAsync(callerToken, pendingOutId);
        // Incoming pending
        await SendFriendRequestAsync(pendingInToken, GetUserIdFromToken(callerToken));

        var results = await SearchAsync(callerToken, "annet");

        // Hot Chocolate serializes GraphQL enums as UPPER_SNAKE_CASE.
        Assert.Equal("FRIENDS", results.First(r => r.Id == friendId).ViewerFriendshipState);
        Assert.Equal("REQUEST_SENT", results.First(r => r.Id == pendingOutId).ViewerFriendshipState);
        Assert.Equal("REQUEST_RECEIVED", results.First(r => r.Id == pendingInId).ViewerFriendshipState);
        Assert.Equal("NONE", results.First(r => r.Id == strangerId).ViewerFriendshipState);
    }

    private GraphQLClient AuthenticatedClient(string token)
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return new GraphQLClient(http);
    }

    private async Task SendFriendRequestAsync(string callerToken, Guid targetId)
    {
        var response = await AuthenticatedClient(callerToken).SendAsync<SendRequestData>(
            "mutation($input: SendFriendRequestInput!) { sendFriendRequest(input: $input) { friendship { id } error } }",
            new { input = new { targetUserId = targetId } });
        var payload = response.RequireData().SendFriendRequest;
        if (payload.Error is not null)
            throw new InvalidOperationException($"sendFriendRequest failed: {payload.Error}");
    }

    private async Task AcceptFriendRequestAsync(string callerToken, Guid requesterId)
    {
        var response = await AuthenticatedClient(callerToken).SendAsync<AcceptData>(
            "mutation($input: RespondToFriendRequestInput!) { acceptFriendRequest(input: $input) { friendship { id } error } }",
            new { input = new { otherUserId = requesterId } });
        var payload = response.RequireData().AcceptFriendRequest;
        if (payload.Error is not null)
            throw new InvalidOperationException($"acceptFriendRequest failed: {payload.Error}");
    }

    private async Task BlockAsync(string callerToken, Guid targetId)
    {
        var response = await AuthenticatedClient(callerToken).SendAsync<BlockData>(
            "mutation($input: BlockUserInput!) { blockUser(input: $input) { success error } }",
            new { input = new { targetUserId = targetId } });
        Assert.True(response.RequireData().BlockUser.Success);
    }

    private async Task FlagAsync(string callerToken, Guid targetId)
    {
        var response = await AuthenticatedClient(callerToken).SendAsync<FlagData>(
            "mutation($input: FlagUserInput!) { flagUser(input: $input) { success error } }",
            new { input = new { targetUserId = targetId } });
        Assert.True(response.RequireData().FlagUser.Success);
    }

    private async Task<List<Guid>> ListPendingAsync(string token, string fieldName)
    {
        var response = await AuthenticatedClient(token).SendAsync<JsonElement>(
            $"query {{ {fieldName}(first: 50) {{ edges {{ node {{ id }} }} }} }}");
        var data = response.RequireData();
        var edges = data.GetProperty(fieldName).GetProperty("edges");
        return edges.EnumerateArray()
            .Select(e => e.GetProperty("node").GetProperty("id").GetGuid())
            .ToList();
    }

    private async Task<List<SearchNode>> SearchAsync(string token, string query)
    {
        var response = await AuthenticatedClient(token).SendAsync<SearchData>(
            "query($q: String!) { searchUsers(query: $q, first: 50) { edges { node { id handle viewerFriendshipState } } } }",
            new { q = query });
        return response.RequireData().SearchUsers.Edges.Select(e => e.Node).ToList();
    }

    private Guid GetUserIdFromToken(string token)
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var graphql = new GraphQLClient(http);
        var response = graphql.SendAsync<CurrentUserData>("query { currentUser { id } }").GetAwaiter().GetResult();
        return response.RequireData().CurrentUser!.Id;
    }

    private async Task SetUserHandleAsync(Guid userId, string handle)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        var user = await dbContext.Users
            .Include(u => u.Profile)
            .FirstAsync(u => u.Id == userId);
        user.EnsureProfile().Handle = handle;
        await dbContext.SaveChangesAsync();
    }

    private async Task<(string Token, Guid UserId)> SignInAsync()
    {
        var email = $"conn-{Guid.NewGuid():N}@buzzkeepr.test";
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

    private sealed record SendRequestData(SendRequestPayload SendFriendRequest);
    private sealed record SendRequestPayload(FriendshipStub? Friendship, string? Error);
    private sealed record FriendshipStub(Guid Id);

    private sealed record AcceptData(AcceptPayload AcceptFriendRequest);
    private sealed record AcceptPayload(FriendshipStub? Friendship, string? Error);

    private sealed record DeclineData(SuccessErrorPayload DeclineFriendRequest);
    private sealed record CancelData(SuccessErrorPayload CancelFriendRequest);
    private sealed record RemoveFriendData(SuccessErrorPayload RemoveFriend);
    private sealed record BlockData(SuccessErrorPayload BlockUser);
    private sealed record UnblockData(SuccessErrorPayload UnblockUser);
    private sealed record FlagData(SuccessErrorPayload FlagUser);
    private sealed record SuccessErrorPayload(bool Success, string? Error);

    private sealed record SearchData(SearchConnection SearchUsers);
    private sealed record SearchConnection(List<SearchEdge> Edges);
    private sealed record SearchEdge(SearchNode Node);
    private sealed record SearchNode(Guid Id, string? Handle, string ViewerFriendshipState);

    private sealed record CurrentUserData(UserStub? CurrentUser);
    private sealed record UserStub(Guid Id);

    private sealed record VerifyData(VerifyPayload VerifyEmailSignIn);
    private sealed record VerifyPayload(VerifyUser? User, VerifySession? Session);
    private sealed record VerifyUser(Guid Id);
    private sealed record VerifySession(string Token);
}
