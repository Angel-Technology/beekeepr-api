using BuzzKeepr.Domain.Enums;

namespace BuzzKeepr.API.GraphQL.Inputs;

public sealed class RegisterPushTokenInput
{
    public string Token { get; init; } = string.Empty;

    public PushPlatform Platform { get; init; }
}
