using BuzzKeepr.Domain.Enums;

namespace BuzzKeepr.Domain.Entities;

public sealed class User
{
    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public bool EmailVerified { get; set; }

    public IdentityVerificationStatus IdentityVerificationStatus { get; set; } = IdentityVerificationStatus.NotStarted;

    public string? PersonaInquiryId { get; set; }

    public PersonaInquiryStatus? PersonaInquiryStatus { get; set; }

    public string? VerifiedFirstName { get; set; }

    public string? VerifiedLastName { get; set; }

    public string? VerifiedBirthdate { get; set; }

    public string? VerifiedAddressStreet1 { get; set; }

    public string? VerifiedAddressStreet2 { get; set; }

    public string? VerifiedAddressCity { get; set; }

    public string? VerifiedAddressSubdivision { get; set; }

    public string? VerifiedAddressPostalCode { get; set; }

    public string? VerifiedCountryCode { get; set; }

    public string? VerifiedLicenseLast4 { get; set; }

    public string? VerifiedLicenseExpirationDate { get; set; }

    public DateTime? PersonaVerifiedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<ExternalAccount> ExternalAccounts { get; set; } = new List<ExternalAccount>();

    public ICollection<Session> Sessions { get; set; } = new List<Session>();

    public ICollection<VerificationToken> VerificationTokens { get; set; } = new List<VerificationToken>();
}
