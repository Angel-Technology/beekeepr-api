namespace BuzzKeepr.Application.Connections.Models;

// Shared shape for accept / decline / cancel. The mutation distinguishes by which service method
// the caller invokes.
public sealed class RespondToFriendRequestResult
{
    public bool RequestNotFound { get; init; }

    public bool Success { get; init; }

    public FriendshipDto? Friendship { get; init; }
}
