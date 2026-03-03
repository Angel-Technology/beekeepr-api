using BuzzKeepr.Application.Users.Models;

namespace BuzzKeepr.API.GraphQL.Mutations;

public sealed class CreateUserPayload
{
    public UserDto? User { get; init; }
    public string? Error { get; init; }
}
