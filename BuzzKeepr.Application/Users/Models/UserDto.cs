namespace BuzzKeepr.Application.Users.Models;

public sealed class UserDto
{
    public Guid Id { get; init; }

    public string Email { get; init; } = string.Empty;

    public string? DisplayName { get; init; }

    public bool EmailVerified { get; init; }

    public DateTime CreatedAtUtc { get; init; }
}