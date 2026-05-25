using BuzzKeepr.Application.Users.Models;
using BuzzKeepr.Domain.Entities;

namespace BuzzKeepr.Application.Users;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<User?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken);

    Task<User?> GetByIdForUpdateIncludingDeletedAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken);

    Task<bool> HandleExistsAsync(string handle, Guid? excludeUserId, CancellationToken cancellationToken);

    // Returns an IQueryable so HotChocolate's [UsePaging] middleware can push skip/take into SQL.
    // `normalizedQuery` must already be trimmed + lower-cased; the repo does not normalize it.
    IQueryable<UserSearchResultDto> Search(string normalizedQuery, Guid? excludeUserId);

    Task AddAsync(User user, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
