namespace BuzzKeepr.API.GraphQL.Inputs;

public sealed class UnregisterPushTokenInput
{
    public string Token { get; init; } = string.Empty;
}
