using BuzzKeepr.Domain.Enums;

namespace BuzzKeepr.API.GraphQL.Mutations;

public sealed class StartPersonaInquiryPayload
{
    public bool Success { get; init; }

    public bool CreatedNewInquiry { get; init; }

    public string? InquiryId { get; init; }

    public string? SessionToken { get; init; }

    public IdentityVerificationStatus? IdentityVerificationStatus { get; init; }

    public PersonaInquiryStatus? PersonaInquiryStatus { get; init; }

    public bool SubscriptionRequired { get; init; }

    public string? Error { get; init; }
}
