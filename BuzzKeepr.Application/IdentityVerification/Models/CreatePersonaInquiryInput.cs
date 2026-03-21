namespace BuzzKeepr.Application.IdentityVerification.Models;

public sealed class CreatePersonaInquiryInput
{
    public string ReferenceId { get; init; } = string.Empty;

    public string? DisplayName { get; init; }

    public string EmailAddress { get; init; } = string.Empty;
}
