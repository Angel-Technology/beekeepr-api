namespace BuzzKeepr.API.GraphQL.Mutations;

public sealed class StartPersonaInquiryPayload
{
    public bool Success { get; init; }

    public bool CreatedNewInquiry { get; init; }

    public string? InquiryId { get; init; }

    public string? IdentityVerificationStatus { get; init; }

    public string? PersonaInquiryStatus { get; init; }

    public string? Error { get; init; }
}
