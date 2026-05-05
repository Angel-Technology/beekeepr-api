namespace BuzzKeepr.Application.IdentityVerification.Models;

public sealed class CreatePersonaSessionTokenResult
{
    public bool Success { get; init; }

    public string? SessionToken { get; init; }

    public string? Error { get; init; }
}
