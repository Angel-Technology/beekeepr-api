namespace BuzzKeepr.API.GraphQL.Inputs;

public sealed class CreateUserInput
{
    public string Email { get; init; } = string.Empty;
}
