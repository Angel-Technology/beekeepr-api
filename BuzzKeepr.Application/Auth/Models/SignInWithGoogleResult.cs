using BuzzKeepr.Application.Users.Models;

namespace BuzzKeepr.Application.Auth.Models;

public sealed class SignInWithGoogleResult
{
    public bool Success { get; init; }

    public bool InvalidInput { get; init; }

    public bool InvalidToken { get; init; }

    public UserDto? User { get; init; }

    public string? SessionToken { get; init; }

    public DateTime? ExpiresAtUtc { get; init; }
}
