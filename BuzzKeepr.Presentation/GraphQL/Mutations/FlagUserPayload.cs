namespace BuzzKeepr.API.GraphQL.Mutations;

public sealed class FlagUserPayload
{
    public bool Success { get; init; }

    public string? Error { get; init; }
}
