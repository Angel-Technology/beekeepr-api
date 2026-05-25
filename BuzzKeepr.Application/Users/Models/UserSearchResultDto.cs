using BuzzKeepr.Domain.Enums;

namespace BuzzKeepr.Application.Users.Models;

// Public-profile projection returned by searchUsers — intentionally excludes email, phone, verified
// names, subscription/billing, identity-verification IDs and any other PII.
public sealed class UserSearchResultDto
{
    public Guid Id { get; init; }

    public string? Handle { get; init; }

    public string? Nickname { get; init; }

    public string? DisplayName { get; init; }

    public string? ImageUrl { get; init; }

    public BackgroundCheckBadge BackgroundCheckBadge { get; init; } = BackgroundCheckBadge.None;

    public DateTime CreatedAtUtc { get; init; }
}
