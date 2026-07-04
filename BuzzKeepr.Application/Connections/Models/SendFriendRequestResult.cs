namespace BuzzKeepr.Application.Connections.Models;

public sealed class SendFriendRequestResult
{
    public bool SelfTarget { get; init; }

    public bool TargetNotFound { get; init; }

    // Caller has blocked target — surface a real error so they know to unblock first.
    public bool BlockedByCaller { get; init; }

    // Target has blocked caller — must not leak this; the GraphQL layer maps it to a generic error.
    public bool BlockedByTarget { get; init; }

    public bool Success { get; init; }

    public FriendshipDto? Friendship { get; init; }
}
