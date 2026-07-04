namespace BuzzKeepr.API.GraphQL.Mutations;

public sealed class DeclineFriendRequestPayload
{
    public bool Success { get; init; }

    public string? Error { get; init; }
}
