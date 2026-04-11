namespace BuzzKeepr.Infrastructure.Configuration;

public sealed class CheckrTrustOptions
{
    public const string SectionName = "CheckrTrust";

    public string ApiBaseUrl { get; init; } = "https://api.checkrtrust.com";

    public string ClientId { get; init; } = string.Empty;

    public string ClientSecret { get; init; } = string.Empty;
}
