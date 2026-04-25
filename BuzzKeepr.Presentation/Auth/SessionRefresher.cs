using BuzzKeepr.Application.Auth;
using BuzzKeepr.Application.Auth.Models;
using Microsoft.AspNetCore.Http;

namespace BuzzKeepr.API.Auth;

public static class SessionRefresher
{
    public static async Task<CurrentUserResult> ResolveAsync(
        HttpContext httpContext,
        IAuthService authService,
        CancellationToken cancellationToken)
    {
        var sessionToken = SessionTokenResolver.Resolve(httpContext);
        var result = await authService.GetCurrentUserAsync(sessionToken, cancellationToken);

        if (result.RefreshedSessionExpiresAtUtc.HasValue
            && !string.IsNullOrWhiteSpace(sessionToken)
            && IsCookieToken(httpContext, sessionToken))
        {
            SessionCookieManager.WriteSessionCookie(
                httpContext,
                sessionToken,
                result.RefreshedSessionExpiresAtUtc.Value);
        }

        return result;
    }

    private static bool IsCookieToken(HttpContext httpContext, string sessionToken)
    {
        var cookieValue = SessionCookieManager.ReadSessionCookie(httpContext);
        return !string.IsNullOrEmpty(cookieValue)
            && string.Equals(cookieValue, sessionToken, StringComparison.Ordinal);
    }
}
