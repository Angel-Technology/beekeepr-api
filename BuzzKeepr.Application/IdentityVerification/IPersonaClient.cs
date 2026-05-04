using BuzzKeepr.Application.IdentityVerification.Models;

namespace BuzzKeepr.Application.IdentityVerification;

public interface IPersonaClient
{
    Task<CreatePersonaInquiryResult> CreateInquiryAsync(
        CreatePersonaInquiryInput input,
        CancellationToken cancellationToken);

    /// <summary>
    /// Mints a one-time-use session token for an existing inquiry so the mobile
    /// SDK can resume it via <c>Inquiry.fromInquiry</c>. Persona's native SDKs
    /// silently fail to launch a server-API-created inquiry without one.
    /// </summary>
    Task<CreatePersonaSessionTokenResult> CreateInquirySessionTokenAsync(
        string inquiryId,
        CancellationToken cancellationToken);
}
