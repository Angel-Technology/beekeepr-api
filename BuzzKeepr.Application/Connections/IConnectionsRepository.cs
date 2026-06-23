using BuzzKeepr.Application.Connections.Models;
using BuzzKeepr.Domain.Entities;

namespace BuzzKeepr.Application.Connections;

public interface IConnectionsRepository
{
    Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken);

    Task<Friendship?> FindFriendshipBetweenAsync(Guid userA, Guid userB, CancellationToken cancellationToken);

    Task<Friendship?> FindPendingRequestAsync(Guid requesterId, Guid addresseeId, CancellationToken cancellationToken);

    Task<UserBlock?> FindBlockAsync(Guid blockerId, Guid blockedId, CancellationToken cancellationToken);

    Task<(bool CallerBlocksTarget, bool TargetBlocksCaller)> CheckBlockedEitherDirectionAsync(
        Guid callerId,
        Guid targetId,
        CancellationToken cancellationToken);

    Task<UserFlag?> FindFlagAsync(Guid flaggerId, Guid flaggedUserId, CancellationToken cancellationToken);

    void AddFriendship(Friendship friendship);

    void RemoveFriendship(Friendship friendship);

    void AddBlock(UserBlock block);

    void RemoveBlock(UserBlock block);

    void AddFlag(UserFlag flag);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    // Wraps SaveChanges with a transaction so flag (insert flag + remove friendship + insert block)
    // is atomic. Returns true on commit, false on rollback (caller handles partial-failure cases).
    Task<bool> ExecuteInTransactionAsync(
        Func<CancellationToken, Task<bool>> work,
        CancellationToken cancellationToken);

    // Projections used by GraphQL list queries. Returned as IQueryable so [UsePaging] can push
    // skip/take into SQL.
    IQueryable<UserConnectionDto> QueryFriends(Guid userId);

    IQueryable<UserConnectionDto> QueryIncomingFriendRequests(Guid userId);

    IQueryable<UserConnectionDto> QueryOutgoingFriendRequests(Guid userId);

    IQueryable<UserConnectionDto> QueryBlockedUsers(Guid userId);

    Task<int> GetFlagCountAsync(Guid flaggedUserId, CancellationToken cancellationToken);
}
