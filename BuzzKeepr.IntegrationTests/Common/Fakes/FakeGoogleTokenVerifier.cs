using BuzzKeepr.Application.Auth;
using BuzzKeepr.Application.Auth.Models;

namespace BuzzKeepr.IntegrationTests.Common.Fakes;

public sealed class FakeGoogleTokenVerifier : IGoogleTokenVerifier
{
    private readonly Dictionary<string, GoogleIdentity?> identitiesByToken = new(StringComparer.Ordinal);

    public Task<GoogleIdentity?> VerifyIdTokenAsync(string idToken, CancellationToken cancellationToken)
    {
        identitiesByToken.TryGetValue(idToken, out var identity);
        return Task.FromResult(identity);
    }

    public void RegisterValidToken(string idToken, GoogleIdentity identity) => identitiesByToken[idToken] = identity;

    public void RegisterInvalidToken(string idToken) => identitiesByToken[idToken] = null;
}
