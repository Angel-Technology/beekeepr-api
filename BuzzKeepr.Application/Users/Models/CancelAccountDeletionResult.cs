namespace BuzzKeepr.Application.Users.Models;

public sealed class CancelAccountDeletionResult
{
    public bool Success { get; init; }

    public bool UserNotFound { get; init; }

    public UserDto? User { get; init; }
}
