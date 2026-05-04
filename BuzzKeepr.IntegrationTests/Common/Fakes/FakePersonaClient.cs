using BuzzKeepr.Application.IdentityVerification;
using BuzzKeepr.Application.IdentityVerification.Models;

namespace BuzzKeepr.IntegrationTests.Common.Fakes;

public sealed class FakePersonaClient : IPersonaClient
{
    public CreatePersonaInquiryResult NextCreateInquiryResult { get; set; } = new()
    {
        Success = true,
        InquiryId = "inq_test_default",
        InquiryStatus = "created"
    };

    public PersonaGovernmentIdDataResult NextGovernmentIdDataResult { get; set; } = new()
    {
        Success = false
    };

    public CreatePersonaSessionTokenResult NextCreateSessionTokenResult { get; set; } = new()
    {
        Success = true,
        SessionToken = "sess_test_default"
    };

    public List<CreatePersonaInquiryInput> CreateInquiryCalls { get; } = new();
    public List<string> GetGovernmentIdDataCalls { get; } = new();
    public List<string> CreateSessionTokenCalls { get; } = new();

    public Task<CreatePersonaInquiryResult> CreateInquiryAsync(
        CreatePersonaInquiryInput input,
        CancellationToken cancellationToken)
    {
        CreateInquiryCalls.Add(input);
        return Task.FromResult(NextCreateInquiryResult);
    }

    public Task<CreatePersonaSessionTokenResult> CreateInquirySessionTokenAsync(
        string inquiryId,
        CancellationToken cancellationToken)
    {
        CreateSessionTokenCalls.Add(inquiryId);
        return Task.FromResult(NextCreateSessionTokenResult);
    }

    public Task<PersonaGovernmentIdDataResult> GetGovernmentIdDataAsync(
        string inquiryId,
        CancellationToken cancellationToken)
    {
        GetGovernmentIdDataCalls.Add(inquiryId);
        return Task.FromResult(NextGovernmentIdDataResult);
    }
}
