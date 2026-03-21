namespace BuzzKeepr.Application.IdentityVerification.Models;

public sealed class PersonaGovernmentIdDataResult
{
    public bool Success { get; init; }

    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    public string? Birthdate { get; init; }

    public string? AddressStreet1 { get; init; }

    public string? AddressStreet2 { get; init; }

    public string? AddressCity { get; init; }

    public string? AddressSubdivision { get; init; }

    public string? AddressPostalCode { get; init; }

    public string? CountryCode { get; init; }

    public string? LicenseNumberLast4 { get; init; }

    public string? LicenseExpirationDate { get; init; }
}
