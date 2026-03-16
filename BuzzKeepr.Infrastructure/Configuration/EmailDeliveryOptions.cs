namespace BuzzKeepr.Infrastructure.Configuration;

public sealed class EmailDeliveryOptions
{
    public const string SectionName = "Email";

    public string FromEmail { get; init; } = "BuzzKeepr <noreply@example.com>";
    public string FrontendBaseUrl { get; init; } = "http://localhost:3000";
    public string ResendApiKey { get; init; } = string.Empty;
    public string ResendBaseUrl { get; init; } = "https://api.resend.com";
}
