namespace BuzzKeepr.Application.IdentityVerification.Models;

public sealed class CreatePersonaInquiryResult
{
    public bool Success { get; init; }

    public string? InquiryId { get; init; }

    public string? InquiryStatus { get; init; }

    public string? Error { get; init; }
}
