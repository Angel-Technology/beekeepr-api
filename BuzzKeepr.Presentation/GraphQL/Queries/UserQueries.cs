using BuzzKeepr.API.Auth;
using BuzzKeepr.Application.Auth;
using BuzzKeepr.Application.Users;
using BuzzKeepr.API.GraphQL.Types;

namespace BuzzKeepr.API.GraphQL.Queries;

public sealed class UserQueries
{
    public async Task<UserGraph?> GetUserByIdAsync(Guid id,
        [Service] IAuthService authService,
        [Service] IUserService userService,
        [Service] IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is required for getUserById.");

        // Row-level security: callers can only fetch their own row through this query.
        // Public profile lookups will land on a separate `userProfile`/`searchUsers` resolver
        // backed by a stripped-down UserProfileGraph (no PII).
        var current = await SessionRefresher.ResolveAsync(httpContext, authService, cancellationToken);
        if (current.User is null || current.User.Id != id)
            return null;

        var user = await userService.GetByIdAsync(id, cancellationToken);
        return user is null ? null : UserGraph.From(user);
    }

    public async Task<UserGraph?> GetCurrentUserAsync(
        [Service] IAuthService authService,
        [Service] IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is required for session lookup.");

        var result = await SessionRefresher.ResolveAsync(httpContext, authService, cancellationToken);
        return result.User is null ? null : UserGraph.From(result.User);
    }
}
