using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace BuzzKeepr.API.Auth;

public static class SessionTokenResolver
{
    public static string? Resolve(HttpContext httpContext)
    {
        if (TryReadBearerToken(httpContext, out var bearerToken))
            return bearerToken;

        return SessionCookieManager.ReadSessionCookie(httpContext);
    }

    private static bool TryReadBearerToken(HttpContext httpContext, out string? token)
    {
        token = null;

        if (!httpContext.Request.Headers.TryGetValue("Authorization", out StringValues authorizationHeader))
            return false;

        var headerValue = authorizationHeader.ToString();

        if (!headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;

        var value = headerValue["Bearer ".Length..].Trim();

        if (string.IsNullOrWhiteSpace(value))
            return false;

        token = value;
        return true;
    }
}
