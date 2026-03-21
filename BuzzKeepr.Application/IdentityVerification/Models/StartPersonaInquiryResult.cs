namespace BuzzKeepr.Application.IdentityVerification.Models;

public sealed class StartPersonaInquiryResult
{
    public bool Success { get; init; }

    public bool CreatedNewInquiry { get; init; }

    public string? InquiryId { get; init; }

    public string IdentityVerificationStatus { get; init; } = "not_started";

    public string? PersonaInquiryStatus { get; init; }

    public string? Error { get; init; }
}
