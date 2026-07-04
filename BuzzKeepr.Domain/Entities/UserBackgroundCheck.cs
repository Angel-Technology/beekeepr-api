using BuzzKeepr.Domain.Enums;

namespace BuzzKeepr.Domain.Entities;

// Checkr-managed background-check state and the resulting badge. Internal — never directly
// exposed to user-facing profile views. The renewal sweeper reads from this table to find
// users whose badge is approaching expiry.
public sealed class UserBackgroundCheck
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public User? User { get; set; }

    public string? CheckrProfileId { get; set; }

    public string? CheckrLastCheckId { get; set; }

    public DateTime? CheckrLastCheckAtUtc { get; set; }

    public bool? CheckrLastCheckHasPossibleMatches { get; set; }

    public BackgroundCheckBadge Badge { get; set; } = BackgroundCheckBadge.None;

    public DateTime? BadgeExpiresAtUtc { get; set; }
}
