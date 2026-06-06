using BuzzKeepr.Application.Auth.Models;

namespace BuzzKeepr.Application.Auth;

public interface IAppleTokenVerifier
{
    Task<AppleIdentity?> VerifyIdTokenAsync(string idToken, CancellationToken cancellationToken);
}
