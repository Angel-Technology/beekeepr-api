namespace BuzzKeepr.Infrastructure.Configuration;

public sealed class AppleAuthOptions
{
    public const string SectionName = "Apple";

    // The `aud` claim on a Sign in with Apple identity token is the iOS app's bundle ID
    // (for native sign-in) or the Services ID (for web). Configure each one we accept
    // here — e.g. com.buzzkeepr.app for the production iOS app.
    public string[] ClientIds { get; init; } = [];
}
