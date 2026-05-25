namespace BuzzKeepr.Application.Billing.Models;

public sealed class RedeemPromoCodeResult
{
    public bool Success { get; init; }

    public bool CodeRequired { get; init; }

    public bool CodeNotFound { get; init; }

    public bool CodeExpired { get; init; }

    public bool CodeInactive { get; init; }

    public bool CodeFullyRedeemed { get; init; }

    public bool AlreadyRedeemed { get; init; }

    public bool GrantFailed { get; init; }

    public bool UserNotFound { get; init; }

    public SubscriptionDto? Subscription { get; init; }
}
