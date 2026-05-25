using BuzzKeepr.Application.Billing.Models;
using BuzzKeepr.Domain.Enums;

namespace BuzzKeepr.Application.Billing;

public interface IRevenueCatClient
{
    Task<RevenueCatSubscriberSnapshot?> GetSubscriberAsync(string appUserId, CancellationToken cancellationToken);

    Task<bool> GrantPromotionalEntitlementAsync(
        string appUserId,
        string entitlementId,
        PromoCodeDuration duration,
        CancellationToken cancellationToken);
}
