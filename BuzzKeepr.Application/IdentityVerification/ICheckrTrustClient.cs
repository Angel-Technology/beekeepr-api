using BuzzKeepr.Application.IdentityVerification.Models;

namespace BuzzKeepr.Application.IdentityVerification;

public interface ICheckrTrustClient
{
    Task<CreateInstantCriminalCheckResult> CreateInstantCriminalCheckAsync(
        CreateInstantCriminalCheckInput input,
        CancellationToken cancellationToken);
}
