namespace BuzzKeepr.Application.Auth.Models;

public sealed class VerifyEmailSignInInput
{
    public string Email { get; init; } = string.Empty;

    public string Code { get; init; } = string.Empty;

    public string? IpAddress { get; init; }

    public string? UserAgent { get; init; }
}
