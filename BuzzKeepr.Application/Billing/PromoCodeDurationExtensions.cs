using BuzzKeepr.Domain.Enums;

namespace BuzzKeepr.Application.Billing;

public static class PromoCodeDurationExtensions
{
    // RevenueCat's /entitlements/{id}/promotional endpoint takes one of these literal duration
    // strings in the request body. Keep this mapping authoritative — the wire format is fixed by
    // RevenueCat's API contract.
    public static string ToRevenueCatDuration(this PromoCodeDuration duration)
    {
        return duration switch
        {
            PromoCodeDuration.Daily => "daily",
            PromoCodeDuration.ThreeDay => "three_day",
            PromoCodeDuration.Weekly => "weekly",
            PromoCodeDuration.Monthly => "monthly",
            PromoCodeDuration.TwoMonth => "two_month",
            PromoCodeDuration.ThreeMonth => "three_month",
            PromoCodeDuration.SixMonth => "six_month",
            PromoCodeDuration.Yearly => "yearly",
            PromoCodeDuration.Lifetime => "lifetime",
            _ => throw new ArgumentOutOfRangeException(nameof(duration), duration, null)
        };
    }
}
