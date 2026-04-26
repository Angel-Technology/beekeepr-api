using BuzzKeepr.Application.Auth;
using BuzzKeepr.Application.Auth.Models;
using Microsoft.AspNetCore.Http;
using Sentry;

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

        if (result.User is not null)
        {
            // Tag the per-request Sentry scope with the user id so any error fired during this
            // request shows up filtered/searchable by user. Email and other PII are intentionally
            // omitted (SendDefaultPii=false) — the id is enough to look the user up internally.
            SentrySdk.ConfigureScope(scope => scope.User = new SentryUser
            {
                Id = result.User.Id.ToString()
            });
        }

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
