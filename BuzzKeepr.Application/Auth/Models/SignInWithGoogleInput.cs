namespace BuzzKeepr.Application.Auth.Models;

public sealed class SignInWithGoogleInput
{
    public string IdToken { get; init; } = string.Empty;
}
