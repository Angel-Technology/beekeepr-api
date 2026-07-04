using BuzzKeepr.Application.Notifications;
using BuzzKeepr.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BuzzKeepr.Infrastructure.Persistence.Repositories;

public sealed class PushTokenRepository(BuzzKeeprDbContext dbContext) : IPushTokenRepository
{
    public Task<UserPushToken?> FindByTokenAsync(string token, CancellationToken cancellationToken)
    {
        return dbContext.UserPushTokens
            .FirstOrDefaultAsync(t => t.Token == token, cancellationToken);
    }

    public async Task<IReadOnlyList<UserPushToken>> GetTokensForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await dbContext.UserPushTokens
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .ToListAsync(cancellationToken);
    }

    public void Add(UserPushToken token) => dbContext.UserPushTokens.Add(token);

    public void Remove(UserPushToken token) => dbContext.UserPushTokens.Remove(token);

    public Task<int> DeleteByTokensAsync(IReadOnlyList<string> tokens, CancellationToken cancellationToken)
    {
        if (tokens.Count == 0) return Task.FromResult(0);

        return dbContext.UserPushTokens
            .Where(t => tokens.Contains(t.Token))
            .ExecuteDeleteAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
