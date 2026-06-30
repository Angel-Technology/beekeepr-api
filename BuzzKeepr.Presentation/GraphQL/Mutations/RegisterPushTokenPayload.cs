namespace BuzzKeepr.API.GraphQL.Mutations;

public sealed class RegisterPushTokenPayload
{
    public bool Success { get; init; }

    public string? Error { get; init; }
}
