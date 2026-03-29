using System.Security.Cryptography;
using System.Text;
using BuzzKeepr.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace BuzzKeepr.Infrastructure.IdentityVerification;

public sealed class PersonaWebhookSignatureVerifier(IOptions<PersonaOptions> personaOptions)
{
    private readonly PersonaOptions options = personaOptions.Value;

    public bool IsValid(string? signatureHeader, string rawRequestBody)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader)
            || string.IsNullOrWhiteSpace(rawRequestBody)
            || options.WebhookSecrets.Length == 0)
        {
            return false;
        }

        foreach (var signatureSet in signatureHeader.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var timestamp = string.Empty;
            var signatures = new List<string>();

            foreach (var part in signatureSet.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var separatorIndex = part.IndexOf('=');

                if (separatorIndex <= 0 || separatorIndex >= part.Length - 1)
                    continue;

                var key = part[..separatorIndex];
                var value = part[(separatorIndex + 1)..];

                if (string.Equals(key, "t", StringComparison.Ordinal))
                    timestamp = value;

                if (string.Equals(key, "v1", StringComparison.Ordinal))
                    signatures.Add(value);
            }

            if (string.IsNullOrWhiteSpace(timestamp) || signatures.Count == 0)
                continue;

            foreach (var secret in options.WebhookSecrets)
            {
                if (string.IsNullOrWhiteSpace(secret))
                    continue;

                var expectedSignature = ComputeSignature(secret, rawRequestBody, timestamp);

                if (signatures.Any(signature => ConstantTimeEquals(signature, expectedSignature)))
                    return true;
            }
        }

        return false;
    }

    private static string ComputeSignature(string secret, string rawRequestBody, string timestamp)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var payload = Encoding.UTF8.GetBytes($"{rawRequestBody}.{timestamp}");
        var hash = hmac.ComputeHash(payload);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool ConstantTimeEquals(string providedSignature, string expectedSignature)
    {
        var left = Encoding.UTF8.GetBytes(providedSignature);
        var right = Encoding.UTF8.GetBytes(expectedSignature);

        return left.Length == right.Length
            && CryptographicOperations.FixedTimeEquals(left, right);
    }
}
