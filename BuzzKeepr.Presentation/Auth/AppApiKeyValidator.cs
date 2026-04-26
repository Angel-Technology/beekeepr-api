using Microsoft.Extensions.Configuration;

namespace BuzzKeepr.API.Auth;

/// <summary>
/// Gate for endpoints that should only be reached by trusted first-party callers (the official frontend app).
/// If <c>Auth:AppApiKey</c> is configured the request must carry an <c>X-App-Api-Key</c> header that matches.
/// If the key is left blank (the dev default) the gate is open — handy for hitting the GraphQL endpoint
/// from Banana Cake Pop or Strawberry Shake without juggling secrets.
/// </summary>
public sealed class AppApiKeyValidator(IConfiguration configuration)
{
    public const string HeaderName = "X-App-Api-Key";

    private readonly string? configuredKey = configuration.GetValue<string?>("Auth:AppApiKey");

    public bool IsValid(HttpContext httpContext)
    {
        if (string.IsNullOrWhiteSpace(configuredKey))
            return true;

        if (!httpContext.Request.Headers.TryGetValue(HeaderName, out var supplied))
            return false;

        var presented = supplied.ToString();
        return !string.IsNullOrEmpty(presented)
            && string.Equals(presented, configuredKey, StringComparison.Ordinal);
    }
}
