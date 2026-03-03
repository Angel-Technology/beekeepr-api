namespace BuzzKeepr.API.Contracts.Users;

public sealed class CreateUserRequest
{
    public string Email { get; init; } = string.Empty;

    public string? DisplayName { get; init; }
}