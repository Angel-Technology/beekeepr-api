using BuzzKeepr.Domain.Entities;

namespace BuzzKeepr.Application.Billing;

public interface IBillingRepository
{
    Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<User?> GetByRevenueCatAppUserIdAsync(string appUserId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
