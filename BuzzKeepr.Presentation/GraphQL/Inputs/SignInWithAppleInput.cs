namespace BuzzKeepr.API.GraphQL.Inputs;

public sealed class SignInWithAppleInput
{
    public string IdToken { get; init; } = string.Empty;

    public string? DisplayName { get; init; }
}
