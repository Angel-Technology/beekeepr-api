namespace BuzzKeepr.Application.IdentityVerification.Models;

public sealed class CreateInstantCriminalCheckInput
{
    public string? ProfileId { get; init; }

    public string FirstName { get; init; } = string.Empty;

    public string? MiddleName { get; init; }

    public string LastName { get; init; } = string.Empty;

    public string? PhoneNumber { get; init; }

    public string? Birthdate { get; init; }

    public string? State { get; init; }
}
