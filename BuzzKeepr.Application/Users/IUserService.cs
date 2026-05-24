using BuzzKeepr.Application.Users.Models;

namespace BuzzKeepr.Application.Users;

public interface IUserService
{
    Task<UserDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<CreateUserResult> CreateAsync(CreateUserInput input, CancellationToken cancellationToken);

    Task<AcceptTermsResult> AcceptTermsAsync(Guid userId, CancellationToken cancellationToken);

    Task<UpdateProfileResult> UpdateProfileAsync(Guid userId, UpdateProfileInput input, CancellationToken cancellationToken);

    Task<RequestAccountDeletionResult> RequestAccountDeletionAsync(Guid userId, CancellationToken cancellationToken);

    Task<CancelAccountDeletionResult> CancelAccountDeletionAsync(Guid userId, CancellationToken cancellationToken);
}
