using BuzzKeepr.API.GraphQL.Inputs;
using BuzzKeepr.API.GraphQL.Types;
using BuzzKeepr.Application.Users;
using ApplicationCreateUserInput = BuzzKeepr.Application.Users.Models.CreateUserInput;

namespace BuzzKeepr.API.GraphQL.Mutations;

public sealed class UserMutations
{
    public async Task<CreateUserPayload> CreateUserAsync(
        CreateUserInput input,
        [Service] IUserService userService,
        CancellationToken cancellationToken)
    {
        var result = await userService.CreateAsync(new ApplicationCreateUserInput
        {
            Email = input.Email,
            DisplayName = input.DisplayName
        }, cancellationToken);

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
            User = new UserGraph
            {
                Id = result.User.Id,
                Email = result.User.Email,
                DisplayName = result.User.DisplayName,
                EmailVerified = result.User.EmailVerified,
                CreatedAtUtc = result.User.CreatedAtUtc
            }
        };
    }
}
