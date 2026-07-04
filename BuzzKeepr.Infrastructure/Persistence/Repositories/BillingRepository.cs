using BuzzKeepr.Application.Billing;
using BuzzKeepr.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BuzzKeepr.Infrastructure.Persistence.Repositories;

public sealed class BillingRepository(BuzzKeeprDbContext dbContext) : IBillingRepository
{
    public Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        return dbContext.Users
            .Include(user => user.Subscription)
            .FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);
    }

    public Task<User?> GetByRevenueCatAppUserIdAsync(string appUserId, CancellationToken cancellationToken)
    {
        // RevenueCatAppUserId now lives on UserSubscription; pivot through the nav.
        return dbContext.Users
            .Include(user => user.Subscription)
            .FirstOrDefaultAsync(
                user => user.Subscription != null
                    && user.Subscription.RevenueCatAppUserId == appUserId,
                cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
