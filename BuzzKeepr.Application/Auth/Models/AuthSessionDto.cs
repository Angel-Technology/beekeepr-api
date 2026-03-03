namespace BuzzKeepr.Application.Auth.Models;

public sealed class AuthSessionDto
{
    public string Token { get; init; } = string.Empty;

    public DateTime ExpiresAtUtc { get; init; }
}