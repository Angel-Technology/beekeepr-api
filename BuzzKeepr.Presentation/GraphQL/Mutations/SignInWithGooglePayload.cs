using BuzzKeepr.API.GraphQL.Types;

namespace BuzzKeepr.API.GraphQL.Mutations;

public sealed class SignInWithGooglePayload
{
    public UserGraph? User { get; init; }

    public AuthSessionGraph? Session { get; init; }

    public string? Error { get; init; }
}
