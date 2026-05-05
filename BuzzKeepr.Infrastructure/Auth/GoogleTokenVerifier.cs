using BuzzKeepr.Application.Auth;
using BuzzKeepr.Application.Auth.Models;
using BuzzKeepr.Infrastructure.Configuration;
using Google.Apis.Auth;
using Microsoft.Extensions.Options;

namespace BuzzKeepr.Infrastructure.Auth;

public sealed class GoogleTokenVerifier(IOptions<GoogleAuthOptions> googleAuthOptions) : IGoogleTokenVerifier
{
    public async Task<GoogleIdentity?> VerifyIdTokenAsync(string idToken, CancellationToken cancellationToken)
    {
        var clientIds = googleAuthOptions.Value.ClientIds
            .Where(clientId => !string.IsNullOrWhiteSpace(clientId))
            .ToArray();

        if (clientIds.Length == 0)
            throw new InvalidOperationException(
                "Google:ClientIds must contain at least one allowed Google OAuth client ID.");

        GoogleJsonWebSignature.Payload payload;

        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(
                idToken,
                new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = clientIds
                });
        }
        catch
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(payload.Subject)
            || string.IsNullOrWhiteSpace(payload.Email)
            || payload.EmailVerified is not true)
        {
            return null;
        }

        return new GoogleIdentity
        {
            ProviderAccountId = payload.Subject,
            Email = payload.Email,
            DisplayName = payload.Name,
            ImageUrl = string.IsNullOrWhiteSpace(payload.Picture) ? null : payload.Picture
        };
    }
}
