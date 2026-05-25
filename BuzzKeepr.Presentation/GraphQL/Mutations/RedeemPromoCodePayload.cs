using BuzzKeepr.Application.Billing.Models;

namespace BuzzKeepr.API.GraphQL.Mutations;

public sealed class RedeemPromoCodePayload
{
    public SubscriptionDto? Subscription { get; init; }

    public string? Error { get; init; }
}
