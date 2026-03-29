using BuzzKeepr.Application.IdentityVerification.Models;

namespace BuzzKeepr.Application.IdentityVerification;

public interface IIdentityVerificationService
{
    Task<StartPersonaInquiryResult> StartPersonaInquiryAsync(Guid userId, CancellationToken cancellationToken);

    Task ProcessPersonaWebhookAsync(string rawRequestBody, CancellationToken cancellationToken);
}
