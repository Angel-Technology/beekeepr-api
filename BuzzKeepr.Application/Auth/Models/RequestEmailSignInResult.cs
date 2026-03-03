namespace BuzzKeepr.Application.Auth.Models;

public sealed class RequestEmailSignInResult
{
    public bool Success { get; init; }

    public bool EmailRequired { get; init; }

    public string Email { get; init; } = string.Empty;

    public DateTime? ExpiresAtUtc { get; init; }

    public string? Token { get; init; }
}