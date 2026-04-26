using BuzzKeepr.Application.Billing.Models;
using BuzzKeepr.Application.Users.Models;
using BuzzKeepr.Domain.Enums;

namespace BuzzKeepr.API.GraphQL.Types;

public sealed class UserGraph
{
    public static UserGraph From(UserDto user) => new()
    {
        Id = user.Id,
        Email = user.Email,
        DisplayName = user.DisplayName,
        ImageUrl = user.ImageUrl,
        EmailVerified = user.EmailVerified,
        IdentityVerificationStatus = user.IdentityVerificationStatus,
        PersonaInquiryId = user.PersonaInquiryId,
        PersonaInquiryStatus = user.PersonaInquiryStatus,
        VerifiedFirstName = user.VerifiedFirstName,
        VerifiedMiddleName = user.VerifiedMiddleName,
        VerifiedLastName = user.VerifiedLastName,
        VerifiedBirthdate = user.VerifiedBirthdate,
        VerifiedLicenseState = user.VerifiedLicenseState,
        PhoneNumber = user.PhoneNumber,
        PersonaVerifiedAtUtc = user.PersonaVerifiedAtUtc,
        BackgroundCheckBadge = user.BackgroundCheckBadge,
        BackgroundCheckBadgeExpiresAtUtc = user.BackgroundCheckBadgeExpiresAtUtc,
        TermsAcceptedAtUtc = user.TermsAcceptedAtUtc,
        Subscription = user.Subscription,
        CreatedAtUtc = user.CreatedAtUtc
    };

    public Guid Id { get; init; }

    public string Email { get; init; } = string.Empty;

    public string? DisplayName { get; init; }

    public string? ImageUrl { get; init; }

    public bool EmailVerified { get; init; }

    public IdentityVerificationStatus IdentityVerificationStatus { get; init; } = IdentityVerificationStatus.NotStarted;

    public string? PersonaInquiryId { get; init; }

    public PersonaInquiryStatus? PersonaInquiryStatus { get; init; }

    public string? VerifiedFirstName { get; init; }

    public string? VerifiedMiddleName { get; init; }

    public string? VerifiedLastName { get; init; }

    public string? VerifiedBirthdate { get; init; }

    public string? VerifiedLicenseState { get; init; }

    public string? PhoneNumber { get; init; }

    public DateTime? PersonaVerifiedAtUtc { get; init; }

    public BackgroundCheckBadge BackgroundCheckBadge { get; init; } = BackgroundCheckBadge.None;

    public DateTime? BackgroundCheckBadgeExpiresAtUtc { get; init; }

    public DateTime? TermsAcceptedAtUtc { get; init; }

    public SubscriptionDto Subscription { get; init; } = new();

    public DateTime CreatedAtUtc { get; init; }
}
