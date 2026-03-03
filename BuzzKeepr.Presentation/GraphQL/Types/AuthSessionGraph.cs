namespace BuzzKeepr.API.GraphQL.Types;

public sealed class AuthSessionGraph
{
    public string Token { get; init; } = string.Empty;

    public DateTime ExpiresAtUtc { get; init; }
}