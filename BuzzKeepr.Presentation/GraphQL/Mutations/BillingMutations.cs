using BuzzKeepr.API.Auth;
using BuzzKeepr.API.GraphQL.Inputs;
using BuzzKeepr.Application.Auth;
using BuzzKeepr.Application.Billing;
using HotChocolate.Types;

namespace BuzzKeepr.API.GraphQL.Mutations;

// Composed onto the existing Mutation root via [ExtendObjectType] + AddTypeExtension<>() in
// Program.cs — keeps billing concerns out of UserMutations without introducing a second
// mutation root type.
[ExtendObjectType(typeof(UserMutations))]
public sealed class BillingMutations
{
    public async Task<RedeemPromoCodePayload> RedeemPromoCodeAsync(
        RedeemPromoCodeInput input,
        [Service] IAuthService authService,
        [Service] IPromoCodeService promoCodeService,
        [Service] IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is required for promo redemption.");

        var currentUser = await SessionRefresher.ResolveAsync(httpContext, authService, cancellationToken);

        if (currentUser.User is null)
        {
            return new RedeemPromoCodePayload
            {
                Error = "Authentication is required."
            };
        }

        var result = await promoCodeService.RedeemAsync(currentUser.User.Id, input.Code, cancellationToken);

        if (result.CodeRequired)
            return new RedeemPromoCodePayload { Error = "Promo code is required." };

        if (result.CodeNotFound)
            return new RedeemPromoCodePayload { Error = "That promo code is not valid." };

        if (result.CodeInactive)
            return new RedeemPromoCodePayload { Error = "That promo code is no longer available." };

        if (result.CodeExpired)
            return new RedeemPromoCodePayload { Error = "That promo code has expired." };

        if (result.CodeFullyRedeemed)
            return new RedeemPromoCodePayload { Error = "That promo code has reached its redemption limit." };

        if (result.AlreadyRedeemed)
            return new RedeemPromoCodePayload { Error = "You have already redeemed this promo code." };

        if (result.UserNotFound)
            return new RedeemPromoCodePayload { Error = "Authentication is required." };

        if (result.GrantFailed)
            return new RedeemPromoCodePayload { Error = "We couldn't apply the promo. Please try again in a moment." };

        if (!result.Success || result.Subscription is null)
            return new RedeemPromoCodePayload { Error = "Promo redemption failed." };

        return new RedeemPromoCodePayload
        {
            Subscription = result.Subscription
        };
    }
}
