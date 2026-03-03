namespace BuzzKeepr.API.GraphQL.Inputs;

public sealed class VerifyEmailSignInInput
{
    public string Email { get; init; } = string.Empty;

    public string Token { get; init; } = string.Empty;
}