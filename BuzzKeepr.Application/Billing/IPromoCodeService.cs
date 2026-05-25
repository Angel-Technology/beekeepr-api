using BuzzKeepr.Application.Billing.Models;

namespace BuzzKeepr.Application.Billing;

public interface IPromoCodeService
{
    Task<RedeemPromoCodeResult> RedeemAsync(Guid userId, string code, CancellationToken cancellationToken);
}
