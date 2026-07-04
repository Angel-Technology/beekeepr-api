using BuzzKeepr.Domain.Enums;

namespace BuzzKeepr.Domain.Entities;

// RevenueCat-mirrored subscription state. Source of truth lives in RevenueCat — this row is a
// local cache that the entitlement gate reads to avoid a round-trip on every request.
public sealed class UserSubscription
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public User? User { get; set; }

    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.None;

    public string? Entitlement { get; set; }

    public string? ProductId { get; set; }

    public SubscriptionStore? Store { get; set; }

    public DateTime? CurrentPeriodEndUtc { get; set; }

    public bool? WillRenew { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public string? RevenueCatAppUserId { get; set; }
}
