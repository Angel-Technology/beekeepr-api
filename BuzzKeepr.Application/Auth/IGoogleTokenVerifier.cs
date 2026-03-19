using BuzzKeepr.Application.Auth.Models;

namespace BuzzKeepr.Application.Auth;

public interface IGoogleTokenVerifier
{
    Task<GoogleIdentity?> VerifyIdTokenAsync(string idToken, CancellationToken cancellationToken);
}
