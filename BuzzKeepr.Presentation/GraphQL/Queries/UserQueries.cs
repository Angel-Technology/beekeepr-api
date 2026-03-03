using BuzzKeepr.Application.Users;
using BuzzKeepr.Application.Users.Models;

namespace BuzzKeepr.API.GraphQL.Queries;

public sealed class UserQueries
{
    public Task<UserDto?> GetUserByIdAsync(Guid id,
        [Service] IUserService userService,
        CancellationToken cancellationToken)
    {
        return userService.GetByIdAsync(id, cancellationToken);
    }
}