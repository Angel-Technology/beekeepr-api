using System.Security.Cryptography;
using System.Text;
using BuzzKeepr.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace BuzzKeepr.Infrastructure.Billing;

// RevenueCat doesn't sign webhooks. Instead they let you set a static "Authorization Header"
// value in their dashboard that they echo back on every POST. We compare it constant-time to the
// configured secret. If you ever need to rotate, set both the dashboard value and our config to
// the new value in lockstep.
public sealed class RevenueCatWebhookAuthorizer(IOptions<RevenueCatOptions> revenueCatOptions)
{
    private readonly RevenueCatOptions options = revenueCatOptions.Value;

    public bool IsValid(string? authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader)
            || string.IsNullOrWhiteSpace(options.WebhookAuthorizationToken))
        {
            return false;
        }

        var provided = Encoding.UTF8.GetBytes(authorizationHeader.Trim());
        var expected = Encoding.UTF8.GetBytes(options.WebhookAuthorizationToken.Trim());

        return provided.Length == expected.Length
            && CryptographicOperations.FixedTimeEquals(provided, expected);
    }
}
