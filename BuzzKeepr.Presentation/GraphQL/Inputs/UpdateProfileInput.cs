namespace BuzzKeepr.API.GraphQL.Inputs;

public sealed class UpdateProfileInput
{
    public string? Nickname { get; init; }

    public string? Handle { get; init; }
}
