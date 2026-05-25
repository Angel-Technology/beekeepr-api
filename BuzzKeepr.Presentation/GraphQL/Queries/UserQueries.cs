using BuzzKeepr.API.Auth;
using BuzzKeepr.Application.Auth;
using BuzzKeepr.Application.Users;
using BuzzKeepr.Application.Users.Models;
using BuzzKeepr.API.GraphQL.Types;
using HotChocolate.Types.Pagination;

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
        // Public profile lookups go through `searchUsers` below, which returns the PII-free
        // UserSearchResultDto projection.
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

    // Authenticated-only public profile search. Ranking + LIMIT/OFFSET run in SQL via [UsePaging],
    // so a typeahead request fetches at most `first` rows.
    [UsePaging(DefaultPageSize = 20, MaxPageSize = 50, IncludeTotalCount = false)]
    public async Task<IQueryable<UserSearchResultDto>> SearchUsersAsync(
        string query,
        [Service] IAuthService authService,
        [Service] IUserService userService,
        [Service] IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is required for searchUsers.");

        var current = await SessionRefresher.ResolveAsync(httpContext, authService, cancellationToken);
        if (current.User is null)
        {
            // No exception — return an empty queryable so the connection still validates. Frontends
            // distinguish "unauthenticated" via the auth error path on other queries, not here.
            return Enumerable.Empty<UserSearchResultDto>().AsQueryable();
        }

        return userService.SearchUsers(query, current.User.Id);
    }

    public async Task<HandleAvailabilityResult> CheckHandleAvailabilityAsync(
        string handle,
        [Service] IAuthService authService,
        [Service] IUserService userService,
        [Service] IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is required for checkHandleAvailability.");

        var current = await SessionRefresher.ResolveAsync(httpContext, authService, cancellationToken);
        if (current.User is null)
            return HandleAvailabilityResult.Unavailable(HandleAvailabilityReasons.AuthenticationRequired);

        return await userService.CheckHandleAvailabilityAsync(handle, current.User.Id, cancellationToken);
    }
}
