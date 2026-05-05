using Microsoft.Extensions.Logging;

namespace BuzzKeepr.API.Auth;

public sealed class CsrfProtectionMiddleware(
    RequestDelegate next,
    ILogger<CsrfProtectionMiddleware> logger)
{
    private const string GraphQLPath = "/graphql";

    public async Task InvokeAsync(HttpContext httpContext, CsrfOriginAllowlist allowlist)
    {
        if (!ShouldEnforce(httpContext))
        {
            await next(httpContext);
            return;
        }

        var hasBearer = httpContext.Request.Headers.TryGetValue("Authorization", out var authHeader)
            && authHeader.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);

        if (hasBearer)
        {
            await next(httpContext);
            return;
        }

        var origin = httpContext.Request.Headers.Origin.ToString();
        var referer = httpContext.Request.Headers.Referer.ToString();

        if (allowlist.IsAllowed(origin) || allowlist.IsAllowed(referer))
        {
            await next(httpContext);
            return;
        }

        logger.LogWarning(
            "CSRF protection blocked {Method} {Path}. Origin={Origin} Referer={Referer}",
            httpContext.Request.Method,
            httpContext.Request.Path,
            string.IsNullOrEmpty(origin) ? "<none>" : origin,
            string.IsNullOrEmpty(referer) ? "<none>" : referer);

        httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
        await httpContext.Response.WriteAsync("Forbidden: cross-origin request rejected by CSRF protection.");
    }

    private static bool ShouldEnforce(HttpContext httpContext)
    {
        if (!HttpMethods.IsPost(httpContext.Request.Method))
            return false;

        if (!httpContext.Request.Path.StartsWithSegments(GraphQLPath, StringComparison.OrdinalIgnoreCase))
            return false;

        var hasCookie = !string.IsNullOrEmpty(SessionCookieManager.ReadSessionCookie(httpContext));
        return hasCookie;
    }
}
