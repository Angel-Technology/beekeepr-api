namespace BuzzKeepr.Application.Auth.Models;

public sealed class VerifyEmailSignInInput
{
    public string Email { get; init; } = string.Empty;

    public string Token { get; init; } = string.Empty;
}