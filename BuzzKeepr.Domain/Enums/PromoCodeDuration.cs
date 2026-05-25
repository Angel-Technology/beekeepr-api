namespace BuzzKeepr.Domain.Enums;

// Mirrors RevenueCat's promotional-entitlement duration values. The string form sent to the
// RevenueCat API lives in BuzzKeepr.Application.Billing.PromoCodeDurationExtensions.
public enum PromoCodeDuration
{
    Daily,
    ThreeDay,
    Weekly,
    Monthly,
    TwoMonth,
    ThreeMonth,
    SixMonth,
    Yearly,
    Lifetime
}
