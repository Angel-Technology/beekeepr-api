using BuzzKeepr.Application.IdentityVerification;
using BuzzKeepr.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BuzzKeepr.Infrastructure.Persistence.Repositories;

public sealed class IdentityVerificationRepository(BuzzKeeprDbContext dbContext) : IIdentityVerificationRepository
{
    public async Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await dbContext.Users
            .FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);
    }

    public async Task<User?> GetByPersonaInquiryIdAsync(string inquiryId, CancellationToken cancellationToken)
    {
        return await dbContext.Users
            .FirstOrDefaultAsync(user => user.PersonaInquiryId == inquiryId, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
