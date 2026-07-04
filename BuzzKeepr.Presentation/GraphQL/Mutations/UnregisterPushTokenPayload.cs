namespace BuzzKeepr.API.GraphQL.Mutations;

public sealed class UnregisterPushTokenPayload
{
    public bool Success { get; init; }

    public string? Error { get; init; }
}
