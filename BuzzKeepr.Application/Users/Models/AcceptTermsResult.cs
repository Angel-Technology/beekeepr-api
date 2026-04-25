namespace BuzzKeepr.Application.Users.Models;

public sealed class AcceptTermsResult
{
    public bool Success { get; init; }

    public bool UserNotFound { get; init; }

    public UserDto? User { get; init; }
}
