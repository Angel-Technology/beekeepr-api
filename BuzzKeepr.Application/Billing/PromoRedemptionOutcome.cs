namespace BuzzKeepr.Application.Billing;

// The three terminal states of the atomic redemption attempt inside the repository. Mapped to
// RedeemPromoCodeResult by the service.
public enum PromoRedemptionOutcome
{
    Success,
    AlreadyRedeemed,
    CapReached,
    GrantFailed
}
