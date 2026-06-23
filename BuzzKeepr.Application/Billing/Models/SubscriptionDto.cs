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
        var sub = user.Subscription;
        return new SubscriptionDto
        {
            Status = sub?.Status ?? SubscriptionStatus.None,
            Entitlement = sub?.Entitlement,
            ProductId = sub?.ProductId,
            Store = sub?.Store,
            CurrentPeriodEndUtc = sub?.CurrentPeriodEndUtc,
            WillRenew = sub?.WillRenew,
            IsActive = IsLocallyActive(user)
        };
    }

    public static bool IsLocallyActive(User user)
    {
        var sub = user.Subscription;
        if (sub is null)
            return false;

        if (sub.Status is SubscriptionStatus.None or SubscriptionStatus.Expired)
            return false;

        return !sub.CurrentPeriodEndUtc.HasValue
            || sub.CurrentPeriodEndUtc.Value > DateTime.UtcNow;
    }
}
