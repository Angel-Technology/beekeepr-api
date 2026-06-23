using BuzzKeepr.Application.IdentityVerification;
using BuzzKeepr.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BuzzKeepr.Infrastructure.Persistence.Repositories;

public sealed class IdentityVerificationRepository(BuzzKeeprDbContext dbContext) : IIdentityVerificationRepository
{
    public async Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Eager-load all four sub-aggregates: the service reads from Profile (DisplayName/Phone),
        // IdentityVerification (the inquiry state it's about to mutate), and BackgroundCheck
        // (existing Checkr profile id), and MapUser/result mapping touches Subscription.
        return await dbContext.Users
            .Include(user => user.Profile)
            .Include(user => user.IdentityVerification)
            .Include(user => user.BackgroundCheck)
            .Include(user => user.Subscription)
            .FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);
    }

    public async Task<User?> GetByPersonaInquiryIdAsync(string inquiryId, CancellationToken cancellationToken)
    {
        // PersonaInquiryId now lives on UserIdentityVerification; pivot through that nav.
        return await dbContext.Users
            .Include(user => user.Profile)
            .Include(user => user.IdentityVerification)
            .Include(user => user.BackgroundCheck)
            .Include(user => user.Subscription)
            .FirstOrDefaultAsync(
                user => user.IdentityVerification != null
                    && user.IdentityVerification.PersonaInquiryId == inquiryId,
                cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
