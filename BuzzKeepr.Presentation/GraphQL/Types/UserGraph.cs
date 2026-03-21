namespace BuzzKeepr.API.GraphQL.Types;

public sealed class UserGraph
{
    public Guid Id { get; init; }

    public string Email { get; init; } = string.Empty;

    public string? DisplayName { get; init; }

    public bool EmailVerified { get; init; }

    public string IdentityVerificationStatus { get; init; } = "not_started";

    public string? PersonaInquiryId { get; init; }

    public string? PersonaInquiryStatus { get; init; }

    public DateTime CreatedAtUtc { get; init; }
}
