namespace BuzzKeepr.Application.Users.Models;

public sealed class RequestAccountDeletionResult
{
    public bool Success { get; init; }

    public bool UserNotFound { get; init; }

    public UserDto? User { get; init; }
}
