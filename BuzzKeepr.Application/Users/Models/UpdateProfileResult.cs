namespace BuzzKeepr.Application.Users.Models;

public sealed class UpdateProfileResult
{
    public bool Success { get; init; }

    public bool UserNotFound { get; init; }

    public bool NicknameTooLong { get; init; }

    public bool HandleInvalid { get; init; }

    public bool HandleAlreadyTaken { get; init; }

    public UserDto? User { get; init; }
}
