using BuzzKeepr.Application.IdentityVerification.Models;

namespace BuzzKeepr.Application.IdentityVerification;

public interface IPersonaClient
{
    Task<CreatePersonaInquiryResult> CreateInquiryAsync(
        CreatePersonaInquiryInput input,
        CancellationToken cancellationToken);

    Task<PersonaGovernmentIdDataResult> GetGovernmentIdDataAsync(
        string inquiryId,
        CancellationToken cancellationToken);
}
