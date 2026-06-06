using System.IdentityModel.Tokens.Jwt;
using BuzzKeepr.Application.Auth;
using BuzzKeepr.Application.Auth.Models;
using BuzzKeepr.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace BuzzKeepr.Infrastructure.Auth;

public sealed class AppleTokenVerifier : IAppleTokenVerifier
{
    private const string AppleIssuer = "https://appleid.apple.com";
    private const string AppleOpenIdConfigurationUrl = "https://appleid.apple.com/.well-known/openid-configuration";
    private const string PrivateRelayDomain = "privaterelay.appleid.com";

    private readonly IOptions<AppleAuthOptions> appleAuthOptions;
    private readonly ILogger<AppleTokenVerifier> logger;
    private readonly ConfigurationManager<OpenIdConnectConfiguration> configurationManager;
    // MapInboundClaims=false keeps standard JWT names (sub, email, email_verified) instead
    // of remapping them to legacy SOAP-style URIs like
    // http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier — which is what
    // the handler does by default and the reason ExtractClaims sees empty Subject/Email.
    private readonly JwtSecurityTokenHandler tokenHandler = new() { MapInboundClaims = false };

    public AppleTokenVerifier(IOptions<AppleAuthOptions> appleAuthOptions, ILogger<AppleTokenVerifier> logger)
    {
        this.appleAuthOptions = appleAuthOptions;
        this.logger = logger;
        // ConfigurationManager caches the OIDC discovery doc + JWKS (default refresh: 24h,
        // automatic re-fetch on signature failure). This is the same pattern AspNetCore's
        // JwtBearer handler uses for issuer key rotation.
        configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            AppleOpenIdConfigurationUrl,
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = true });
    }

    public async Task<AppleIdentity?> VerifyIdTokenAsync(string idToken, CancellationToken cancellationToken)
    {
        var clientIds = appleAuthOptions.Value.ClientIds
            .Where(clientId => !string.IsNullOrWhiteSpace(clientId))
            .ToArray();

        if (clientIds.Length == 0)
            throw new InvalidOperationException(
                "Apple:ClientIds must contain at least one allowed Apple client ID (iOS bundle ID or Services ID).");

        OpenIdConnectConfiguration configuration;
        try
        {
            configuration = await configurationManager.GetConfigurationAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to fetch Apple OIDC configuration/JWKS.");
            return null;
        }

        var validationParameters = new TokenValidationParameters
        {
            ValidIssuer = AppleIssuer,
            ValidateIssuer = true,
            ValidAudiences = clientIds,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = configuration.SigningKeys,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(5)
        };

        ClaimsValidationResult result;
        try
        {
            var validationResult = await tokenHandler.ValidateTokenAsync(idToken, validationParameters);
            if (!validationResult.IsValid)
            {
                // The inner exception type (SecurityTokenInvalidAudienceException,
                // ...ExpiredException, ...) is the only signal worth keeping — it tells us
                // whether the next failure is a misconfigured bundle ID, an expired token,
                // or a deeper issue. PII (email, sub) and full tokens stay out of logs.
                logger.LogWarning(
                    "Apple identity token rejected: {Reason}.",
                    validationResult.Exception?.GetType().Name ?? "unknown");
                return null;
            }

            result = ExtractClaims(validationResult.Claims);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Unexpected error validating Apple identity token.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(result.Subject) || string.IsNullOrWhiteSpace(result.Email))
            return null;

        return new AppleIdentity
        {
            ProviderAccountId = result.Subject,
            Email = result.Email,
            EmailVerified = result.EmailVerified,
            IsPrivateRelayEmail = result.Email.EndsWith($"@{PrivateRelayDomain}", StringComparison.OrdinalIgnoreCase)
        };
    }

    private static ClaimsValidationResult ExtractClaims(IDictionary<string, object> claims)
    {
        return new ClaimsValidationResult
        {
            Subject = claims.TryGetValue("sub", out var sub) ? sub?.ToString() ?? string.Empty : string.Empty,
            Email = claims.TryGetValue("email", out var email) ? email?.ToString() ?? string.Empty : string.Empty,
            // Apple sends email_verified as either a bool or the string "true"/"false"
            // depending on the token. Normalize both.
            EmailVerified = claims.TryGetValue("email_verified", out var verified) && IsTruthy(verified)
        };
    }

    private static bool IsTruthy(object? value)
    {
        return value switch
        {
            bool b => b,
            string s => string.Equals(s, "true", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private sealed class ClaimsValidationResult
    {
        public string Subject { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public bool EmailVerified { get; init; }
    }
}
