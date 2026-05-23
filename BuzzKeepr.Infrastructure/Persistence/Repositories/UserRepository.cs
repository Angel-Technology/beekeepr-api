using BuzzKeepr.Application.Users;
using BuzzKeepr.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BuzzKeepr.Infrastructure.Persistence.Repositories;

public sealed class UserRepository(BuzzKeeprDbContext dbContext) : IUserRepository
{
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
