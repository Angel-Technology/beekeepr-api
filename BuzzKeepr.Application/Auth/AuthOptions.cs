namespace BuzzKeepr.Application.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// App-store review accounts. When an email in this list requests an email sign-in code,
    /// we persist a verification token whose hash matches the configured PIN and skip the
    /// actual email send. Reviewers (Apple/Google) can then sign in without us needing to
    /// relay a real inbox to them. Remove the entry once review is approved.
    ///
    /// Modeled as a list (not a Dictionary keyed by email) so it binds cleanly from Render
    /// env vars — Render rejects env-var keys containing '@', which a Dictionary-keyed-by-email
    /// approach would require. Bind via <c>Auth__ReviewAccounts__0__Email</c> /
    /// <c>Auth__ReviewAccounts__0__Pin</c>, increment the index for additional reviewers.
    /// </summary>
    public List<ReviewAccount> ReviewAccounts { get; init; } = new();
}

public sealed class ReviewAccount
{
    public string Email { get; init; } = string.Empty;

    public string Pin { get; init; } = string.Empty;
}
