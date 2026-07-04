namespace BuzzKeepr.API.GraphQL.Inputs;

public sealed class StartInstantCriminalCheckInput
{
    public string? PhoneNumber { get; init; }

    public string? LicenseState { get; init; }
}
