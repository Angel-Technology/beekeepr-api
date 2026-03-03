using BuzzKeepr.Application.Users;
using BuzzKeepr.API.GraphQL.Types;

namespace BuzzKeepr.API.GraphQL.Queries;

public sealed class UserQueries
{
    public async Task<UserGraph?> GetUserByIdAsync(Guid id,
        [Service] IUserService userService,
        CancellationToken cancellationToken)
    {
        var user = await userService.GetByIdAsync(id, cancellationToken);

        return user is null
            ? null
            : new UserGraph
            {
                Id = user.Id,
                Email = user.Email,
                DisplayName = user.DisplayName,
                EmailVerified = user.EmailVerified,
                CreatedAtUtc = user.CreatedAtUtc
            };
    }
}
