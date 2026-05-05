namespace BuzzKeepr.Application.IdentityVerification.Models;

public sealed class PersonaGovernmentIdDataResult
{
    public bool Success { get; init; }

    public string? FirstName { get; init; }

    public string? MiddleName { get; init; }

    public string? LastName { get; init; }

    public string? Birthdate { get; init; }

    public string? LicenseState { get; init; }
}
