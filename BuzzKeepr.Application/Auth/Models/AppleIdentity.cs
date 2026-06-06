namespace BuzzKeepr.Application.Auth.Models;

public sealed class AppleIdentity
{
    public string ProviderAccountId { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public bool EmailVerified { get; init; }

    public bool IsPrivateRelayEmail { get; init; }
}
