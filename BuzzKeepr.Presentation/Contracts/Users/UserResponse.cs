namespace BuzzKeepr.API.Contracts.Users;

public sealed class UserResponse
{
    public Guid Id { get; init; }

    public string Email { get; init; } = string.Empty;

    public string? DisplayName { get; init; }

    public bool EmailVerified { get; init; }

    public DateTime CreatedAtUtc { get; init; }
}