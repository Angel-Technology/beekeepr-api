using BuzzKeepr.Domain.Enums;

namespace BuzzKeepr.Application.Billing.Models;

// Result of GET /v1/subscribers/{app_user_id}, projected down to just the fields we mirror.
public sealed class RevenueCatSubscriberSnapshot
{
    public string AppUserId { get; init; } = string.Empty;

    public SubscriptionStatus Status { get; init; } = SubscriptionStatus.None;

    public string? Entitlement { get; init; }

    public string? ProductId { get; init; }

    public SubscriptionStore? Store { get; init; }

    public DateTime? CurrentPeriodEndUtc { get; init; }

    public bool? WillRenew { get; init; }
}
