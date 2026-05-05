namespace BuzzKeepr.Infrastructure.Configuration;

public sealed class CheckrTrustOptions
{
    public const string SectionName = "CheckrTrust";

    public string ApiBaseUrl { get; init; } = "https://api.checkrtrust.com";

    public string ClientId { get; init; } = string.Empty;

    public string ClientSecret { get; init; } = string.Empty;

    /// <summary>
    /// Checkr Trust rulesets to apply on every check. Sent as <c>ruleset_ids</c> in the
    /// Create Check request body. Multiple rulesets are merged + de-duplicated server-side
    /// — typical setup is one ruleset per policy slice (e.g. one for felonies all-time,
    /// another for recent misdemeanors).
    /// </summary>
    public string[] RulesetIds { get; init; } = [];
}
