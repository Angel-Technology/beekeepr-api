namespace BuzzKeepr.Application.Auth.Models;

public sealed class AppleIdentity
{
    public string ProviderAccountId { get; init; } = string.Empty;

    // Apple only sends the email claim on the FIRST authorization per Apple ID / App ID pair.
    // Subsequent sign-ins return a valid token with `sub` but no `email` — hence nullable.
    public string? Email { get; init; }

    public bool EmailVerified { get; init; }

    public bool IsPrivateRelayEmail { get; init; }
}
