namespace BuzzKeepr.API.GraphQL.Inputs;

public sealed class StartInstantCriminalCheckInput
{
    public string FirstName { get; init; } = string.Empty;

    public string? MiddleName { get; init; }

    public string LastName { get; init; } = string.Empty;

    public string? PhoneNumber { get; init; }

    public string? DateOfBirth { get; init; }

    public string? State { get; init; }
}
