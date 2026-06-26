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
        Nickname = user.Nickname,
        Handle = user.Handle,
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
        GoogleVoicePhone = user.GoogleVoicePhone,
        WhatsAppPhone = user.WhatsAppPhone,
        InstagramHandle = user.InstagramHandle,
        TelegramHandle = user.TelegramHandle,
        SignalPhone = user.SignalPhone,
        ProfileVisibility = user.ProfileVisibility,
        ContactVisibility = user.ContactVisibility,
        PersonaVerifiedAtUtc = user.PersonaVerifiedAtUtc,
        BackgroundCheckBadge = user.BackgroundCheckBadge,
        BackgroundCheckBadgeExpiresAtUtc = user.BackgroundCheckBadgeExpiresAtUtc,
        CheckrLastCheckAtUtc = user.CheckrLastCheckAtUtc,
        TermsAcceptedAtUtc = user.TermsAcceptedAtUtc,
        Subscription = user.Subscription,
        CreatedAtUtc = user.CreatedAtUtc,
        DeletedAtUtc = user.DeletedAtUtc
    };

    public Guid Id { get; init; }

    public string Email { get; init; } = string.Empty;

    public string? DisplayName { get; init; }

    public string? Nickname { get; init; }

    public string? Handle { get; init; }

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

    public string? GoogleVoicePhone { get; init; }

    public string? WhatsAppPhone { get; init; }

    public string? InstagramHandle { get; init; }

    public string? TelegramHandle { get; init; }

    public string? SignalPhone { get; init; }

    public ProfileVisibility ProfileVisibility { get; init; } = ProfileVisibility.Public;

    public ContactVisibility ContactVisibility { get; init; } = ContactVisibility.Private;

    public DateTime? PersonaVerifiedAtUtc { get; init; }

    public BackgroundCheckBadge BackgroundCheckBadge { get; init; } = BackgroundCheckBadge.None;

    public DateTime? BackgroundCheckBadgeExpiresAtUtc { get; init; }

    public DateTime? CheckrLastCheckAtUtc { get; init; }

    public DateTime? TermsAcceptedAtUtc { get; init; }

    public SubscriptionDto Subscription { get; init; } = new();

    public DateTime CreatedAtUtc { get; init; }

    public DateTime? DeletedAtUtc { get; init; }
}
