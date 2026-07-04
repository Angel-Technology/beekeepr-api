namespace BuzzKeepr.API.GraphQL.Mutations;

public sealed class RemoveFriendPayload
{
    public bool Success { get; init; }

    public string? Error { get; init; }
}
