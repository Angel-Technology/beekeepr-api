namespace BuzzKeepr.Domain.Entities;

// Core identity row. Holds only what auth and lifecycle actually need:
// - Email + verification flag (the credential)
// - Terms acceptance (legal gate)
// - Welcome-email tracking (sweeper bookkeeping)
// - Created / soft-delete timestamps
//
// Everything user-facing, provider-derived, billing, or compliance-related lives on a 1:1
// sub-aggregate (UserProfile / UserIdentityVerification / UserBackgroundCheck / UserSubscription)
// so this row stays small and the App Store-sensitive "fetched vs user-supplied" boundary
// is structural, not a documentation comment.
public sealed class User
{
    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public bool EmailVerified { get; set; }

    public DateTime? TermsAcceptedAtUtc { get; set; }

    public DateTime? WelcomeEmailSentAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? DeletedAtUtc { get; set; }

    public ICollection<ExternalAccount> ExternalAccounts { get; set; } = new List<ExternalAccount>();

    public ICollection<Session> Sessions { get; set; } = new List<Session>();

    public ICollection<VerificationToken> VerificationTokens { get; set; } = new List<VerificationToken>();

    public UserProfile? Profile { get; set; }

    public UserIdentityVerification? IdentityVerification { get; set; }

    public UserBackgroundCheck? BackgroundCheck { get; set; }

    public UserSubscription? Subscription { get; set; }
}
