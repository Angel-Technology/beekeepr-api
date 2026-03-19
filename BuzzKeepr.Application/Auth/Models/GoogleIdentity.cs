namespace BuzzKeepr.Application.Auth.Models;

public sealed class GoogleIdentity
{
    public string ProviderAccountId { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string? DisplayName { get; init; }
}
