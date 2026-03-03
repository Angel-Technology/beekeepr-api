using BuzzKeepr.Application.Users.Models;

namespace BuzzKeepr.Application.Users;

public interface IUserService
{
    Task<UserDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<CreateUserResult> CreateAsync(CreateUserInput input, CancellationToken cancellationToken);
}