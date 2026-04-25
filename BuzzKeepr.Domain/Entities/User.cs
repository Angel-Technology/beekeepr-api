using BuzzKeepr.Domain.Enums;

namespace BuzzKeepr.Domain.Entities;

public sealed class User
{
    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public string? ImageUrl { get; set; }

    public bool EmailVerified { get; set; }

    public IdentityVerificationStatus IdentityVerificationStatus { get; set; } = IdentityVerificationStatus.NotStarted;

    public string? PersonaInquiryId { get; set; }

    public PersonaInquiryStatus? PersonaInquiryStatus { get; set; }

    public DateTime? PersonaInquiryUpdatedAtUtc { get; set; }

    public string? VerifiedFirstName { get; set; }

    public string? VerifiedMiddleName { get; set; }

    public string? VerifiedLastName { get; set; }

    public string? VerifiedBirthdate { get; set; }

    public string? VerifiedLicenseState { get; set; }

    public string? PhoneNumber { get; set; }

    public DateTime? PersonaVerifiedAtUtc { get; set; }

    public string? CheckrProfileId { get; set; }

    public string? CheckrLastCheckId { get; set; }

    public DateTime? CheckrLastCheckAtUtc { get; set; }

    public bool? CheckrLastCheckHasPossibleMatches { get; set; }

    public DateTime? TermsAcceptedAtUtc { get; set; }

    public DateTime? WelcomeEmailSentAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<ExternalAccount> ExternalAccounts { get; set; } = new List<ExternalAccount>();

    public ICollection<Session> Sessions { get; set; } = new List<Session>();

    public ICollection<VerificationToken> VerificationTokens { get; set; } = new List<VerificationToken>();
}
