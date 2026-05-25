using BuzzKeepr.Application.Users;
using BuzzKeepr.Application.Users.Models;
using BuzzKeepr.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BuzzKeepr.Infrastructure.Persistence.Repositories;

public sealed class UserRepository(BuzzKeeprDbContext dbContext) : IUserRepository
{
    // Trigram similarity below this is dropped from results — empirically ~0.3 catches obvious typos
    // ("samul" -> "samuel") without flooding results with unrelated names. Tune if needed.
    private const double TrigramSimilarityFloor = 0.3;

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.Id == id, cancellationToken);
    }

    public async Task<User?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.Users
            .FirstOrDefaultAsync(user => user.Id == id, cancellationToken);
    }

    public async Task<User?> GetByIdForUpdateIncludingDeletedAsync(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(user => user.Id == id, cancellationToken);
    }

    public async Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters: emails of pending-deletion users are still in the unique index;
        // pretending they don't exist would cause an INSERT to fail at the DB layer.
        return await dbContext.Users
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(user => user.Email == email, cancellationToken);
    }

    public async Task<bool> HandleExistsAsync(string handle, Guid? excludeUserId, CancellationToken cancellationToken)
    {
        return await dbContext.Users
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(user => user.Handle == handle && (excludeUserId == null || user.Id != excludeUserId), cancellationToken);
    }

    public IQueryable<UserSearchResultDto> Search(string normalizedQuery, Guid? excludeUserId)
    {
        var prefixPattern = normalizedQuery + "%";

        // Soft-deleted rows are filtered by the global query filter on the User entity, so they're
        // excluded here automatically. Ranking is computed in SQL so [UsePaging] can do LIMIT/OFFSET
        // on the already-ordered, already-projected query.
        return dbContext.Users
            .AsNoTracking()
            .Where(user => excludeUserId == null || user.Id != excludeUserId)
            .Where(user =>
                (user.Handle != null && (user.Handle == normalizedQuery || EF.Functions.ILike(user.Handle, prefixPattern)))
                || (user.Nickname != null && EF.Functions.TrigramsSimilarity(user.Nickname, normalizedQuery) >= TrigramSimilarityFloor)
                || (user.DisplayName != null && EF.Functions.TrigramsSimilarity(user.DisplayName, normalizedQuery) >= TrigramSimilarityFloor))
            .OrderBy(user =>
                user.Handle == normalizedQuery ? 0 :
                user.Handle != null && EF.Functions.ILike(user.Handle, prefixPattern) ? 1 :
                2)
            .ThenByDescending(user =>
                (user.Nickname != null ? EF.Functions.TrigramsSimilarity(user.Nickname, normalizedQuery) : 0d)
                + (user.DisplayName != null ? EF.Functions.TrigramsSimilarity(user.DisplayName, normalizedQuery) : 0d))
            .ThenBy(user => user.Id) // stable tiebreaker so cursor pagination doesn't skip/duplicate
            .Select(user => new UserSearchResultDto
            {
                Id = user.Id,
                Handle = user.Handle,
                Nickname = user.Nickname,
                DisplayName = user.DisplayName,
                ImageUrl = user.ImageUrl,
                BackgroundCheckBadge = user.BackgroundCheckBadge,
            });
    }

    public async Task AddAsync(User user, CancellationToken cancellationToken)
    {
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
