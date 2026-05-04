using BuzzKeepr.Domain.Enums;

namespace BuzzKeepr.Application.IdentityVerification.Models;

public sealed class StartPersonaInquiryResult
{
    public bool Success { get; init; }

    public bool CreatedNewInquiry { get; init; }

    public string? InquiryId { get; init; }

    public string? SessionToken { get; init; }

    public IdentityVerificationStatus IdentityVerificationStatus { get; init; } = IdentityVerificationStatus.NotStarted;

    public PersonaInquiryStatus? PersonaInquiryStatus { get; init; }

    public bool SubscriptionRequired { get; init; }

    public string? Error { get; init; }
}
