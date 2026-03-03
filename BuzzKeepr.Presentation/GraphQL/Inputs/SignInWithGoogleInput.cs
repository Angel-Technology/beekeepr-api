namespace BuzzKeepr.API.GraphQL.Inputs;

public sealed class SignInWithGoogleInput
{
    public string Email { get; init; } = string.Empty;

    public string ProviderAccountId { get; init; } = string.Empty;

    public string? DisplayName { get; init; }
}