using BuzzKeepr.Application.Auth;
using BuzzKeepr.Application.Auth.Models;

namespace BuzzKeepr.IntegrationTests.Common.Fakes;

public sealed class FakeAppleTokenVerifier : IAppleTokenVerifier
{
    private readonly Dictionary<string, AppleIdentity?> identitiesByToken = new(StringComparer.Ordinal);

    public Task<AppleIdentity?> VerifyIdTokenAsync(string idToken, CancellationToken cancellationToken)
    {
        identitiesByToken.TryGetValue(idToken, out var identity);
        return Task.FromResult(identity);
    }

    public void RegisterValidToken(string idToken, AppleIdentity identity) => identitiesByToken[idToken] = identity;

    public void RegisterInvalidToken(string idToken) => identitiesByToken[idToken] = null;
}
