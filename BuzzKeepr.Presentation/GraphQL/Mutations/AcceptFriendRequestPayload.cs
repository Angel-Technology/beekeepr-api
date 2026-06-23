using BuzzKeepr.API.GraphQL.Types;

namespace BuzzKeepr.API.GraphQL.Mutations;

public sealed class AcceptFriendRequestPayload
{
    public FriendshipGraph? Friendship { get; init; }

    public string? Error { get; init; }
}
