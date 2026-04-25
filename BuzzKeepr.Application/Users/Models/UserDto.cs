using BuzzKeepr.Domain.Enums;

namespace BuzzKeepr.Application.Users.Models;

public sealed class UserDto
{
    public Guid Id { get; init; }

    public string Email { get; init; } = string.Empty;

    public string? DisplayName { get; init; }

    public bool EmailVerified { get; init; }

    public IdentityVerificationStatus IdentityVerificationStatus { get; init; } = IdentityVerificationStatus.NotStarted;

    public string? PersonaInquiryId { get; init; }

    public PersonaInquiryStatus? PersonaInquiryStatus { get; init; }

    public DateTime? TermsAcceptedAtUtc { get; init; }

    public DateTime CreatedAtUtc { get; init; }
}
