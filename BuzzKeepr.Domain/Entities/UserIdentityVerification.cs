using BuzzKeepr.Domain.Enums;

namespace BuzzKeepr.Domain.Entities;

// Persona-managed identity verification state. The `Verified*` name/birthdate/state fields
// are sourced from Persona's government-ID extraction and exist for downstream compliance use
// (e.g. as Checkr inputs). They are never exposed back to the user as "the user's name" —
// that's what UserProfile is for.
public sealed class UserIdentityVerification
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public User? User { get; set; }

    public IdentityVerificationStatus Status { get; set; } = IdentityVerificationStatus.NotStarted;

    public string? PersonaInquiryId { get; set; }

    public PersonaInquiryStatus? PersonaInquiryStatus { get; set; }

    public DateTime? PersonaInquiryUpdatedAtUtc { get; set; }

    public string? VerifiedFirstName { get; set; }

    public string? VerifiedMiddleName { get; set; }

    public string? VerifiedLastName { get; set; }

    public string? VerifiedBirthdate { get; set; }

    public string? VerifiedLicenseState { get; set; }

    public DateTime? PersonaVerifiedAtUtc { get; set; }
}
