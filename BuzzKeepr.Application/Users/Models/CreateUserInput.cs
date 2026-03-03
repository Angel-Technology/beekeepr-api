namespace BuzzKeepr.Application.Users.Models;

public sealed class CreateUserInput
{
    public string Email { get; init; } = string.Empty;

    public string? DisplayName { get; init; }
}