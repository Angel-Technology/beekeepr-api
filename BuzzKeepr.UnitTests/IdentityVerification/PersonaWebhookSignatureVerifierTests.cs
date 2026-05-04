using System.Security.Cryptography;
using System.Text;
using BuzzKeepr.Infrastructure.Configuration;
using BuzzKeepr.Infrastructure.IdentityVerification;
using Microsoft.Extensions.Options;

namespace BuzzKeepr.UnitTests.IdentityVerification;

public sealed class PersonaWebhookSignatureVerifierTests
{
    private const string Secret = "wbhsec_known_secret";
    private const string OtherSecret = "wbhsec_rotated_secret";
    private const string Body = """{"data":{"attributes":{"payload":{"data":{"type":"inquiry","id":"inq_1","attributes":{"status":"approved"}}}}}}""";
    private const string Timestamp = "1700000000";

    [Fact]
    public void IsValid_WithCorrectlySignedPayload_ReturnsTrue()
    {
        var verifier = CreateVerifier(Secret);
        var signature = ComputeSignature(Secret, Body, Timestamp);

        Assert.True(verifier.IsValid($"t={Timestamp},v1={signature}", Body));
    }

    [Fact]
    public void IsValid_WithWrongSecret_ReturnsFalse()
    {
        var verifier = CreateVerifier(Secret);
        var signature = ComputeSignature("wbhsec_attacker_guess", Body, Timestamp);

        Assert.False(verifier.IsValid($"t={Timestamp},v1={signature}", Body));
    }

    [Fact]
    public void IsValid_WhenAnyConfiguredSecretMatches_ReturnsTrue()
    {
        var verifier = CreateVerifier(Secret, OtherSecret);
        var signature = ComputeSignature(OtherSecret, Body, Timestamp);

        Assert.True(verifier.IsValid($"t={Timestamp},v1={signature}", Body));
    }

    [Fact]
    public void IsValid_WithMissingHeader_ReturnsFalse()
    {
        var verifier = CreateVerifier(Secret);

        Assert.False(verifier.IsValid(null, Body));
        Assert.False(verifier.IsValid("", Body));
    }

    [Fact]
    public void IsValid_WithMalformedHeader_ReturnsFalse()
    {
        var verifier = CreateVerifier(Secret);

        Assert.False(verifier.IsValid("not-a-real-signature", Body));
    }

    [Fact]
    public void IsValid_WithMutatedBody_ReturnsFalse()
    {
        var verifier = CreateVerifier(Secret);
        var signature = ComputeSignature(Secret, Body, Timestamp);

        Assert.False(verifier.IsValid($"t={Timestamp},v1={signature}", Body + "tampered"));
    }

    private static PersonaWebhookSignatureVerifier CreateVerifier(params string[] secrets)
    {
        var options = Options.Create(new PersonaOptions
        {
            ApiKey = "irrelevant",
            ApiBaseUrl = "https://persona.test.invalid",
            InquiryTemplateId = "itmpl_irrelevant",
            WebhookSecrets = secrets
        });
        return new PersonaWebhookSignatureVerifier(options);
    }

    private static string ComputeSignature(string secret, string body, string timestamp)
    {
        // Persona's real format is `${timestamp}.${body}` — keep this in sync with the verifier.
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
