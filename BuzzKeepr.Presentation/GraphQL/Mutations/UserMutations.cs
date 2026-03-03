using BuzzKeepr.Application.Users;
using BuzzKeepr.Application.Users.Models;

namespace BuzzKeepr.API.GraphQL.Mutations;

public sealed class UserMutations
{
    public async Task<CreateUserPayload> CreateUserAsync(
        CreateUserInput input,
        [Service] IUserService userService,
        CancellationToken cancellationToken)
    {
        var result = await userService.CreateAsync(input, cancellationToken);

        if (result.EmailRequired)
        {
            return new CreateUserPayload
            {
                Error = "Email is required."
            };
        }

        if (result.EmailAlreadyExists)
        {
            return new CreateUserPayload
            {
                Error = "A user with that email already exists."
            };
        }

        if (!result.Success || result.User is null)
        {
            return new CreateUserPayload
            {
                Error = "User creation failed."
            };
        }

        return new CreateUserPayload
        {
            User = result.User
        };
    }
}
