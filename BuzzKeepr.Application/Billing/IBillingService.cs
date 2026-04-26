using BuzzKeepr.Application.Billing.Models;

namespace BuzzKeepr.Application.Billing;

public interface IBillingService
{
    Task ProcessRevenueCatWebhookAsync(string rawRequestBody, CancellationToken cancellationToken);

    // Used for server-side gating of paid actions. Reads the cached mirror first; if the mirror
    // says the user isn't entitled, we fall back to a live REST call before denying the action,
    // because a freshly-purchased user may not have had their webhook arrive yet.
    Task<SubscriptionDto> GetSubscriptionForUserAsync(Guid userId, CancellationToken cancellationToken);
}
