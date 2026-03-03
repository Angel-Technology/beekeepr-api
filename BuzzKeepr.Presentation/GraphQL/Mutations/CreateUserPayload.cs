using BuzzKeepr.API.GraphQL.Types;

namespace BuzzKeepr.API.GraphQL.Mutations;

public sealed class CreateUserPayload
{
    public UserGraph? User { get; init; }

    public string? Error { get; init; }
}