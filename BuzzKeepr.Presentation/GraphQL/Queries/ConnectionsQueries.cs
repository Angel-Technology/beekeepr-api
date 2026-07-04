using BuzzKeepr.API.Auth;
using BuzzKeepr.Application.Auth;
using BuzzKeepr.Application.Connections;
using BuzzKeepr.Application.Connections.Models;
using HotChocolate.Types;
using HotChocolate.Types.Pagination;

namespace BuzzKeepr.API.GraphQL.Queries;

// Extends the root Query type with friends / pending-request / blocked-users lists. Same
// [UsePaging] cursor pattern as searchUsers — skip/take lands in SQL.
[ExtendObjectType(typeof(UserQueries))]
public sealed class ConnectionsQueries
{
    [UsePaging(DefaultPageSize = 20, MaxPageSize = 50, IncludeTotalCount = false)]
    public async Task<IQueryable<UserConnectionDto>> GetFriendsAsync(
        [Service] IAuthService authService,
        [Service] IConnectionsService connectionsService,
        [Service] IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var current = await ResolveCurrentUserAsync(authService, httpContextAccessor, cancellationToken);
        if (current is null)
            return Enumerable.Empty<UserConnectionDto>().AsQueryable();

        return connectionsService.ListFriends(current.Value);
    }

    [UsePaging(DefaultPageSize = 20, MaxPageSize = 50, IncludeTotalCount = false)]
    public async Task<IQueryable<UserConnectionDto>> GetIncomingFriendRequestsAsync(
        [Service] IAuthService authService,
        [Service] IConnectionsService connectionsService,
        [Service] IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var current = await ResolveCurrentUserAsync(authService, httpContextAccessor, cancellationToken);
        if (current is null)
            return Enumerable.Empty<UserConnectionDto>().AsQueryable();

        return connectionsService.ListIncomingFriendRequests(current.Value);
    }

    [UsePaging(DefaultPageSize = 20, MaxPageSize = 50, IncludeTotalCount = false)]
    public async Task<IQueryable<UserConnectionDto>> GetOutgoingFriendRequestsAsync(
        [Service] IAuthService authService,
        [Service] IConnectionsService connectionsService,
        [Service] IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var current = await ResolveCurrentUserAsync(authService, httpContextAccessor, cancellationToken);
        if (current is null)
            return Enumerable.Empty<UserConnectionDto>().AsQueryable();

        return connectionsService.ListOutgoingFriendRequests(current.Value);
    }

    [UsePaging(DefaultPageSize = 20, MaxPageSize = 50, IncludeTotalCount = false)]
    public async Task<IQueryable<UserConnectionDto>> GetBlockedUsersAsync(
        [Service] IAuthService authService,
        [Service] IConnectionsService connectionsService,
        [Service] IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var current = await ResolveCurrentUserAsync(authService, httpContextAccessor, cancellationToken);
        if (current is null)
            return Enumerable.Empty<UserConnectionDto>().AsQueryable();

        return connectionsService.ListBlockedUsers(current.Value);
    }

    private static async Task<Guid?> ResolveCurrentUserAsync(
        IAuthService authService,
        IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is required for connection queries.");

        var current = await SessionRefresher.ResolveAsync(httpContext, authService, cancellationToken);
        return current.User?.Id;
    }
}
