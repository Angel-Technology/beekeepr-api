namespace BuzzKeepr.Application.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// Email → fixed PIN map for app-store review accounts. When an email here requests
    /// an email sign-in code, we persist a verification token whose hash matches the
    /// configured PIN and skip the actual email send. Reviewers (Apple/Google) can then
    /// sign in without us needing to relay a real inbox to them. Remove the entry once
    /// review is approved.
    /// </summary>
    public Dictionary<string, string> ReviewAccounts { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
