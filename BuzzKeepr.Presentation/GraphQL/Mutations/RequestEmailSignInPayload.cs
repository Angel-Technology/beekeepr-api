namespace BuzzKeepr.API.GraphQL.Mutations;

public sealed class RequestEmailSignInPayload
{
    public bool Success { get; init; }

    public string? Email { get; init; }

    public DateTime? ExpiresAtUtc { get; init; }

    public string? DevelopmentToken { get; init; }

    public string? Error { get; init; }
}