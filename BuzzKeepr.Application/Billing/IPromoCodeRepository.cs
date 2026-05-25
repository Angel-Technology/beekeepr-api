using BuzzKeepr.Domain.Entities;

namespace BuzzKeepr.Application.Billing;

public interface IPromoCodeRepository
{
    Task<PromoCode?> FindByCodeAsync(string code, CancellationToken cancellationToken);

    // Wraps the three steps that have to happen atomically — insert the redemption row, increment
    // the code's used-count under the cap predicate, then call the grant callback — inside one DB
    // transaction. If any step fails the transaction is rolled back, so failing the grant doesn't
    // leave a redemption row behind. The grant callback returning false (or throwing) is treated
    // as failure.
    Task<PromoRedemptionOutcome> TryRedeemAsync(
        Guid promoCodeId,
        Guid userId,
        Func<CancellationToken, Task<bool>> grantCallback,
        CancellationToken cancellationToken);
}
