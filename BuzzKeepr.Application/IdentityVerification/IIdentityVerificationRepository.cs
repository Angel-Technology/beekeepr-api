using BuzzKeepr.Domain.Entities;

namespace BuzzKeepr.Application.IdentityVerification;

public interface IIdentityVerificationRepository
{
    Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<User?> GetByPersonaInquiryIdAsync(string inquiryId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
