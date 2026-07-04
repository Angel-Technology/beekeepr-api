namespace BuzzKeepr.API.GraphQL.Mutations;

public sealed class UnblockUserPayload
{
    public bool Success { get; init; }

    public string? Error { get; init; }
}
