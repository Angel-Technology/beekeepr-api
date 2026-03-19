namespace BuzzKeepr.API.GraphQL.Inputs;

public sealed class SignInWithGoogleInput
{
    public string IdToken { get; init; } = string.Empty;
}
