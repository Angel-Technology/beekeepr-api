using BuzzKeepr.API.Auth;
using BuzzKeepr.API.GraphQL.Inputs;
using BuzzKeepr.API.GraphQL.Types;
using BuzzKeepr.Application.Auth;
using BuzzKeepr.Application.Connections;
using HotChocolate.Types;

namespace BuzzKeepr.API.GraphQL.Mutations;

// Composed onto the existing Mutation root via [ExtendObjectType] + AddTypeExtension<>() in
// Program.cs — keeps the social graph operations together without bloating UserMutations.
[ExtendObjectType(typeof(UserMutations))]
public sealed class ConnectionsMutations
{
    // BlockedByTarget and TargetNotFound are intentionally collapsed to the same generic message so
    // the caller can't probe for "is this user blocking me?" or "does this user exist?".
    private const string GenericSendError = "Unable to send friend request.";

    public async Task<SendFriendRequestPayload> SendFriendRequestAsync(
        SendFriendRequestInput input,
        [Service] IAuthService authService,
        [Service] IConnectionsService connectionsService,
        [Service] IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is required for sendFriendRequest.");

        var current = await SessionRefresher.ResolveAsync(httpContext, authService, cancellationToken);
        if (current.User is null)
            return new SendFriendRequestPayload { Error = "Authentication is required." };

        var result = await connectionsService.SendFriendRequestAsync(
            current.User.Id, input.TargetUserId, cancellationToken);

        if (result.SelfTarget)
            return new SendFriendRequestPayload { Error = "You cannot send a friend request to yourself." };

        if (result.BlockedByCaller)
            return new SendFriendRequestPayload { Error = "Unblock this user before sending a friend request." };

        if (result.TargetNotFound || result.BlockedByTarget)
            return new SendFriendRequestPayload { Error = GenericSendError };

        if (!result.Success || result.Friendship is null)
            return new SendFriendRequestPayload { Error = "Unable to send friend request." };

        return new SendFriendRequestPayload { Friendship = FriendshipGraph.From(result.Friendship) };
    }

    public async Task<AcceptFriendRequestPayload> AcceptFriendRequestAsync(
        RespondToFriendRequestInput input,
        [Service] IAuthService authService,
        [Service] IConnectionsService connectionsService,
        [Service] IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is required for acceptFriendRequest.");

        var current = await SessionRefresher.ResolveAsync(httpContext, authService, cancellationToken);
        if (current.User is null)
            return new AcceptFriendRequestPayload { Error = "Authentication is required." };

        var result = await connectionsService.AcceptFriendRequestAsync(
            current.User.Id, input.OtherUserId, cancellationToken);

        if (result.RequestNotFound)
            return new AcceptFriendRequestPayload { Error = "No pending friend request from that user." };

        if (!result.Success || result.Friendship is null)
            return new AcceptFriendRequestPayload { Error = "Unable to accept friend request." };

        return new AcceptFriendRequestPayload { Friendship = FriendshipGraph.From(result.Friendship) };
    }

    public async Task<DeclineFriendRequestPayload> DeclineFriendRequestAsync(
        RespondToFriendRequestInput input,
        [Service] IAuthService authService,
        [Service] IConnectionsService connectionsService,
        [Service] IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is required for declineFriendRequest.");

        var current = await SessionRefresher.ResolveAsync(httpContext, authService, cancellationToken);
        if (current.User is null)
            return new DeclineFriendRequestPayload { Error = "Authentication is required." };

        var result = await connectionsService.DeclineFriendRequestAsync(
            current.User.Id, input.OtherUserId, cancellationToken);

        if (result.RequestNotFound)
            return new DeclineFriendRequestPayload { Error = "No pending friend request from that user." };

        return new DeclineFriendRequestPayload { Success = result.Success };
    }

    public async Task<CancelFriendRequestPayload> CancelFriendRequestAsync(
        RespondToFriendRequestInput input,
        [Service] IAuthService authService,
        [Service] IConnectionsService connectionsService,
        [Service] IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is required for cancelFriendRequest.");

        var current = await SessionRefresher.ResolveAsync(httpContext, authService, cancellationToken);
        if (current.User is null)
            return new CancelFriendRequestPayload { Error = "Authentication is required." };

        var result = await connectionsService.CancelFriendRequestAsync(
            current.User.Id, input.OtherUserId, cancellationToken);

        if (result.RequestNotFound)
            return new CancelFriendRequestPayload { Error = "No outgoing friend request to that user." };

        return new CancelFriendRequestPayload { Success = result.Success };
    }

    public async Task<RemoveFriendPayload> RemoveFriendAsync(
        RemoveFriendInput input,
        [Service] IAuthService authService,
        [Service] IConnectionsService connectionsService,
        [Service] IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is required for removeFriend.");

        var current = await SessionRefresher.ResolveAsync(httpContext, authService, cancellationToken);
        if (current.User is null)
            return new RemoveFriendPayload { Error = "Authentication is required." };

        var result = await connectionsService.RemoveFriendAsync(
            current.User.Id, input.OtherUserId, cancellationToken);

        return new RemoveFriendPayload { Success = result.Success };
    }

    public async Task<BlockUserPayload> BlockUserAsync(
        BlockUserInput input,
        [Service] IAuthService authService,
        [Service] IConnectionsService connectionsService,
        [Service] IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is required for blockUser.");

        var current = await SessionRefresher.ResolveAsync(httpContext, authService, cancellationToken);
        if (current.User is null)
            return new BlockUserPayload { Error = "Authentication is required." };

        var result = await connectionsService.BlockUserAsync(
            current.User.Id, input.TargetUserId, cancellationToken);

        if (result.SelfTarget)
            return new BlockUserPayload { Error = "You cannot block yourself." };

        if (result.TargetNotFound)
            return new BlockUserPayload { Error = "Unable to block user." };

        return new BlockUserPayload { Success = result.Success };
    }

    public async Task<UnblockUserPayload> UnblockUserAsync(
        UnblockUserInput input,
        [Service] IAuthService authService,
        [Service] IConnectionsService connectionsService,
        [Service] IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is required for unblockUser.");

        var current = await SessionRefresher.ResolveAsync(httpContext, authService, cancellationToken);
        if (current.User is null)
            return new UnblockUserPayload { Error = "Authentication is required." };

        var result = await connectionsService.UnblockUserAsync(
            current.User.Id, input.TargetUserId, cancellationToken);

        return new UnblockUserPayload { Success = result.Success };
    }

    public async Task<FlagUserPayload> FlagUserAsync(
        FlagUserInput input,
        [Service] IAuthService authService,
        [Service] IConnectionsService connectionsService,
        [Service] IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is required for flagUser.");

        var current = await SessionRefresher.ResolveAsync(httpContext, authService, cancellationToken);
        if (current.User is null)
            return new FlagUserPayload { Error = "Authentication is required." };

        var result = await connectionsService.FlagUserAsync(
            current.User.Id, input.TargetUserId, cancellationToken);

        if (result.SelfTarget)
            return new FlagUserPayload { Error = "You cannot flag yourself." };

        if (result.TargetNotFound)
            return new FlagUserPayload { Error = "Unable to flag user." };

        return new FlagUserPayload { Success = result.Success };
    }
}
