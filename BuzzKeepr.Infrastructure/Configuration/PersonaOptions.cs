namespace BuzzKeepr.Infrastructure.Configuration;

public sealed class PersonaOptions
{
    public const string SectionName = "Persona";

    public string ApiKey { get; init; } = string.Empty;

    public string ApiBaseUrl { get; init; } = "https://api.withpersona.com";

    public string InquiryTemplateId { get; init; } = string.Empty;

    /// <summary>
    /// Optional Persona theme set id (e.g. <c>theset_xxx</c>) applied to every
    /// inquiry we mint. Set via <c>Persona:ThemeSetId</c> / <c>Persona__ThemeSetId</c>.
    /// Leave empty to inherit whatever default the template has on the
    /// dashboard.
    /// </summary>
    public string ThemeSetId { get; init; } = string.Empty;

    public string[] WebhookSecrets { get; init; } = [];
}
