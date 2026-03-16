using Microsoft.AspNetCore.Http;

namespace BuzzKeepr.API.Auth;

public static class SessionCookieManager
{
    public const string SessionCookieName = "buzzkeepr_session";

    public static void WriteSessionCookie(HttpContext httpContext, string sessionToken, DateTime expiresAtUtc)
    {
        var isHttps = httpContext.Request.IsHttps;

        httpContext.Response.Cookies.Append(SessionCookieName, sessionToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = isHttps ? SameSiteMode.None : SameSiteMode.Lax,
            Expires = new DateTimeOffset(expiresAtUtc)
        });
    }

    public static string? ReadSessionCookie(HttpContext httpContext)
    {
        return httpContext.Request.Cookies.TryGetValue(SessionCookieName, out var value) ? value : null;
    }

    public static void ClearSessionCookie(HttpContext httpContext)
    {
        httpContext.Response.Cookies.Delete(SessionCookieName);
    }
}
