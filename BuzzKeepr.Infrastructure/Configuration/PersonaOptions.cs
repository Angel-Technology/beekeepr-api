namespace BuzzKeepr.Infrastructure.Configuration;

public sealed class PersonaOptions
{
    public const string SectionName = "Persona";

    public string ApiKey { get; init; } = string.Empty;

    public string ApiBaseUrl { get; init; } = "https://api.withpersona.com";

    public string InquiryTemplateId { get; init; } = string.Empty;

    public string[] WebhookSecrets { get; init; } = [];
}
