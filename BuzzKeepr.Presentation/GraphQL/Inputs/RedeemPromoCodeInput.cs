namespace BuzzKeepr.API.GraphQL.Inputs;

public sealed class RedeemPromoCodeInput
{
    public string Code { get; init; } = string.Empty;
}
