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

    // Returns an unmaterialized IQueryable; the caller (GraphQL resolver) applies cursor pagination
    // via [UsePaging], so we let LIMIT/OFFSET land in SQL instead of pulling rows into memory.
    IQueryable<UserSearchResultDto> SearchUsers(string query, Guid? excludeUserId);
}
