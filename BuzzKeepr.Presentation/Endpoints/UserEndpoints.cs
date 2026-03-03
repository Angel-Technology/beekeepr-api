using BuzzKeepr.API.Contracts.Users;
using BuzzKeepr.Application.Users;
using BuzzKeepr.Application.Users.Models;

namespace BuzzKeepr.API.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users")
            .WithTags("Users");

        group.MapGet("/{id:guid}", GetUserById)
            .WithName("GetUserById");

        group.MapPost("/", CreateUser)
            .WithName("CreateUser");

        return app;
    }

    private static async Task<IResult> GetUserById(Guid id, IUserService userService, CancellationToken cancellationToken)
    {
        var user = await userService.GetByIdAsync(id, cancellationToken);

        return user is null
            ? Results.NotFound()
            : Results.Ok(MapUserResponse(user));
    }

    private static async Task<IResult> CreateUser(
        CreateUserRequest request,
        IUserService userService,
        CancellationToken cancellationToken)
    {
        var result = await userService.CreateAsync(new CreateUserInput
        {
            Email = request.Email,
            DisplayName = request.DisplayName
        }, cancellationToken);

        if (result.EmailRequired)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["email"] = ["Email is required."]
            });
        }

        if (result.EmailAlreadyExists)
        {
            return Results.Conflict(new
            {
                Message = "A user with that email already exists."
            });
        }

        if (!result.Success || result.User is null)
        {
            return Results.Problem("User creation failed.");
        }

        return Results.Created($"/api/users/{result.User.Id}", MapUserResponse(result.User));
    }

    private static UserResponse MapUserResponse(UserDto user)
    {
        return new UserResponse
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            EmailVerified = user.EmailVerified,
            CreatedAtUtc = user.CreatedAtUtc
        };
    }
}
