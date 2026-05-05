using BuzzKeepr.Application.IdentityVerification;
using BuzzKeepr.Application.IdentityVerification.Models;

namespace BuzzKeepr.IntegrationTests.Common.Fakes;

public sealed class FakeCheckrTrustClient : ICheckrTrustClient
{
    public CreateInstantCriminalCheckResult NextResult { get; set; } = new()
    {
        Success = true,
        CheckId = "chk_test_default",
        ProfileId = "prf_test_default",
        ResultCount = 0,
        HasPossibleMatches = false
    };

    public List<CreateInstantCriminalCheckInput> Calls { get; } = new();

    public Task<CreateInstantCriminalCheckResult> CreateInstantCriminalCheckAsync(
        CreateInstantCriminalCheckInput input,
        CancellationToken cancellationToken)
    {
        Calls.Add(input);
        return Task.FromResult(NextResult);
    }
}
