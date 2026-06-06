namespace BuzzKeepr.Application.Auth.Models;

public sealed class SignInWithAppleInput
{
    public string IdToken { get; init; } = string.Empty;

    // Apple only returns the user's name on the FIRST sign-in, in the authorization
    // response (not the JWT). The client (expo-apple-authentication) must capture it then
    // and forward it here so we can populate DisplayName on the new User row.
    public string? DisplayName { get; init; }

    public string? IpAddress { get; init; }

    public string? UserAgent { get; init; }
}
