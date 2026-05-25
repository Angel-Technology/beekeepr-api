using BuzzKeepr.Application.Billing.Models;
using Microsoft.Extensions.Logging;

namespace BuzzKeepr.Application.Billing;

public sealed class PromoCodeService(
    IPromoCodeRepository promoCodeRepository,
    IBillingRepository billingRepository,
    IBillingService billingService,
    IRevenueCatClient revenueCatClient,
    ILogger<PromoCodeService> logger) : IPromoCodeService
{
    public async Task<RedeemPromoCodeResult> RedeemAsync(Guid userId, string code, CancellationToken cancellationToken)
    {
        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(normalized))
            return new RedeemPromoCodeResult { CodeRequired = true };

        var promo = await promoCodeRepository.FindByCodeAsync(normalized, cancellationToken);

        if (promo is null)
            return new RedeemPromoCodeResult { CodeNotFound = true };

        if (!promo.IsActive)
            return new RedeemPromoCodeResult { CodeInactive = true };

        if (promo.ExpiresAtUtc.HasValue && promo.ExpiresAtUtc.Value <= DateTime.UtcNow)
            return new RedeemPromoCodeResult { CodeExpired = true };

        // Cheap up-front rejection so a clearly-exhausted code doesn't spin up a transaction.
        // The authoritative cap check lives in the atomic UPDATE inside TryRedeemAsync.
        if (promo.MaxRedemptions.HasValue && promo.RedemptionsUsed >= promo.MaxRedemptions.Value)
            return new RedeemPromoCodeResult { CodeFullyRedeemed = true };

        var user = await billingRepository.GetByIdAsync(userId, cancellationToken);

        if (user is null)
            return new RedeemPromoCodeResult { UserNotFound = true };

        // Frontend convention (see BillingService.TryResolveUserByAppUserIdAsync): the RevenueCat
        // appUserId equals our user.Id as a Guid string when the SDK hasn't seen this user yet.
        var revenueCatAppUserId = string.IsNullOrWhiteSpace(user.RevenueCatAppUserId)
            ? userId.ToString()
            : user.RevenueCatAppUserId;

        // Lazy-create the RevenueCat subscriber. GET /v1/subscribers/{id} is idempotent and
        // creates the record if missing, which is required before the promotional-grant POST
        // can attach an entitlement — otherwise it 404s with "subscriber was not found" for
        // users whose frontend SDK hasn't yet called Purchases.logIn(user.id). Return value is
        // intentionally ignored; we only need the side effect.
        _ = await revenueCatClient.GetSubscriberAsync(revenueCatAppUserId, cancellationToken);

        // Stamp the app_user_id on the local mirror if it was never set. This makes the
        // post-redemption GetSubscriptionForUserAsync call below actually do its live read
        // (it short-circuits when RevenueCatAppUserId is null) and saves future flows from
        // having to re-resolve via the Guid-string fallback. Save outside of TryRedeemAsync's
        // transaction so the stamp persists even if redemption later rolls back.
        if (string.IsNullOrWhiteSpace(user.RevenueCatAppUserId))
        {
            user.RevenueCatAppUserId = revenueCatAppUserId;
            await billingRepository.SaveChangesAsync(cancellationToken);
        }

        var outcome = await promoCodeRepository.TryRedeemAsync(
            promo.Id,
            userId,
            grantCt => revenueCatClient.GrantPromotionalEntitlementAsync(
                revenueCatAppUserId,
                promo.EntitlementId,
                promo.Duration,
                grantCt),
            cancellationToken);

        switch (outcome)
        {
            case PromoRedemptionOutcome.AlreadyRedeemed:
                return new RedeemPromoCodeResult { AlreadyRedeemed = true };

            case PromoRedemptionOutcome.CapReached:
                return new RedeemPromoCodeResult { CodeFullyRedeemed = true };

            case PromoRedemptionOutcome.GrantFailed:
                logger.LogWarning(
                    "RevenueCat promotional grant failed for user {UserId} on code {Code} (entitlement {Entitlement}, duration {Duration}).",
                    userId,
                    promo.Code,
                    promo.EntitlementId,
                    promo.Duration);
                return new RedeemPromoCodeResult { GrantFailed = true };
        }

        // Refresh the local subscription mirror by calling the existing live-read path. Without
        // this, the next currentUser query would still show the user as unsubscribed until the
        // RevenueCat webhook lands.
        var subscription = await billingService.GetSubscriptionForUserAsync(userId, cancellationToken);

        logger.LogInformation(
            "Promo code {Code} redeemed by user {UserId} → entitlement {Entitlement}, duration {Duration}.",
            promo.Code,
            userId,
            promo.EntitlementId,
            promo.Duration);

        return new RedeemPromoCodeResult
        {
            Success = true,
            Subscription = subscription
        };
    }
}
