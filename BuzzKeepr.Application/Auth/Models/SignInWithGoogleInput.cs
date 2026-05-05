namespace BuzzKeepr.Application.Auth.Models;

public sealed class SignInWithGoogleInput
{
    public string IdToken { get; init; } = string.Empty;

    public string? IpAddress { get; init; }

    public string? UserAgent { get; init; }
}
