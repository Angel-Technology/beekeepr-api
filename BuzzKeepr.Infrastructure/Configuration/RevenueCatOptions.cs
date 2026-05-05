namespace BuzzKeepr.Infrastructure.Configuration;

public sealed class RevenueCatOptions
{
    public const string SectionName = "RevenueCat";

    public string ApiBaseUrl { get; init; } = "https://api.revenuecat.com";

    public string SecretApiKey { get; init; } = string.Empty;

    public string WebhookAuthorizationToken { get; init; } = string.Empty;
}
