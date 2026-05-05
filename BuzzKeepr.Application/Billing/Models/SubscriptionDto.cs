using BuzzKeepr.Domain.Entities;
using BuzzKeepr.Domain.Enums;

namespace BuzzKeepr.Application.Billing.Models;

public sealed class SubscriptionDto
{
    public SubscriptionStatus Status { get; init; } = SubscriptionStatus.None;

    public string? Entitlement { get; init; }

    public string? ProductId { get; init; }

    public SubscriptionStore? Store { get; init; }

    public DateTime? CurrentPeriodEndUtc { get; init; }

    public bool? WillRenew { get; init; }

    // Convenience: the frontend can read this directly to gate paid surfaces without
    // re-implementing "is this user actually entitled right now?" logic.
    public bool IsActive { get; init; }

    public static SubscriptionDto FromUser(User user)
    {
        return new SubscriptionDto
        {
            Status = user.SubscriptionStatus,
            Entitlement = user.SubscriptionEntitlement,
            ProductId = user.SubscriptionProductId,
            Store = user.SubscriptionStore,
            CurrentPeriodEndUtc = user.SubscriptionCurrentPeriodEndUtc,
            WillRenew = user.SubscriptionWillRenew,
            IsActive = IsLocallyActive(user)
        };
    }

    public static bool IsLocallyActive(User user)
    {
        if (user.SubscriptionStatus is SubscriptionStatus.None or SubscriptionStatus.Expired)
            return false;

        return !user.SubscriptionCurrentPeriodEndUtc.HasValue
            || user.SubscriptionCurrentPeriodEndUtc.Value > DateTime.UtcNow;
    }
}
