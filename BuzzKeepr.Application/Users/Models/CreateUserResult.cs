namespace BuzzKeepr.Application.Users.Models;

public sealed class CreateUserResult
{
    public bool Success { get; init; }

    public bool EmailRequired { get; init; }

    public bool EmailAlreadyExists { get; init; }

    public UserDto? User { get; init; }
}