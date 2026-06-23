using BuzzKeepr.Application.Connections;
using BuzzKeepr.Application.Connections.Models;
using BuzzKeepr.Domain.Entities;
using BuzzKeepr.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BuzzKeepr.Infrastructure.Persistence.Repositories;

public sealed class ConnectionsRepository(BuzzKeeprDbContext dbContext) : IConnectionsRepository
{
    // Postgres SQLSTATE 23505 = unique_violation. In this aggregate it fires when two concurrent
    // block/flag/friend-request inserts race the (Blocker,Blocked) / (Flagger,Flagged) /
    // (Requester,Addressee) unique index. The end-state matches what the caller asked for, so we
    // treat it as success.
    private const string UniqueViolationSqlState = "23505";

    public Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Global query filter excludes soft-deleted users — friend/block/flag actions should not
        // target accounts in the deletion grace window.
        return dbContext.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == userId, cancellationToken);
    }

    public Task<Friendship?> FindFriendshipBetweenAsync(Guid userA, Guid userB, CancellationToken cancellationToken)
    {
        return dbContext.Friendships
            .FirstOrDefaultAsync(friendship =>
                (friendship.RequesterId == userA && friendship.AddresseeId == userB)
                || (friendship.RequesterId == userB && friendship.AddresseeId == userA),
                cancellationToken);
    }

    public Task<Friendship?> FindPendingRequestAsync(Guid requesterId, Guid addresseeId, CancellationToken cancellationToken)
    {
        return dbContext.Friendships
            .FirstOrDefaultAsync(friendship =>
                friendship.RequesterId == requesterId
                && friendship.AddresseeId == addresseeId
                && friendship.Status == FriendshipStatus.Pending,
                cancellationToken);
    }

    public Task<UserBlock?> FindBlockAsync(Guid blockerId, Guid blockedId, CancellationToken cancellationToken)
    {
        return dbContext.UserBlocks
            .FirstOrDefaultAsync(block => block.BlockerId == blockerId && block.BlockedId == blockedId, cancellationToken);
    }

    public async Task<(bool CallerBlocksTarget, bool TargetBlocksCaller)> CheckBlockedEitherDirectionAsync(
        Guid callerId,
        Guid targetId,
        CancellationToken cancellationToken)
    {
        // One round-trip — fetch any block row in either direction, then bucket in memory.
        var rows = await dbContext.UserBlocks
            .AsNoTracking()
            .Where(block =>
                (block.BlockerId == callerId && block.BlockedId == targetId)
                || (block.BlockerId == targetId && block.BlockedId == callerId))
            .Select(block => new { block.BlockerId, block.BlockedId })
            .ToListAsync(cancellationToken);

        var callerBlocksTarget = rows.Any(r => r.BlockerId == callerId && r.BlockedId == targetId);
        var targetBlocksCaller = rows.Any(r => r.BlockerId == targetId && r.BlockedId == callerId);
        return (callerBlocksTarget, targetBlocksCaller);
    }

    public Task<UserFlag?> FindFlagAsync(Guid flaggerId, Guid flaggedUserId, CancellationToken cancellationToken)
    {
        return dbContext.UserFlags
            .FirstOrDefaultAsync(flag => flag.FlaggerId == flaggerId && flag.FlaggedUserId == flaggedUserId, cancellationToken);
    }

    public void AddFriendship(Friendship friendship) => dbContext.Friendships.Add(friendship);

    public void RemoveFriendship(Friendship friendship) => dbContext.Friendships.Remove(friendship);

    public void AddBlock(UserBlock block) => dbContext.UserBlocks.Add(block);

    public void RemoveBlock(UserBlock block) => dbContext.UserBlocks.Remove(block);

    public void AddFlag(UserFlag flag) => dbContext.UserFlags.Add(flag);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    public async Task<bool> ExecuteInTransactionAsync(
        Func<CancellationToken, Task<bool>> work,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var ok = await work(cancellationToken);
            if (ok)
                await transaction.CommitAsync(cancellationToken);
            else
                await transaction.RollbackAsync(cancellationToken);
            return ok;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg
                                           && pg.SqlState == UniqueViolationSqlState)
        {
            // A racing insert beat the pre-check inside the transaction. The block/flag row that
            // the caller wanted already exists — end-state is correct, so report success.
            await transaction.RollbackAsync(cancellationToken);
            // Reset the tracker so the rest of the request scope can still use this DbContext;
            // otherwise EF Core's change-tracker keeps the failed Add in the queue.
            foreach (var entry in dbContext.ChangeTracker.Entries().ToList())
            {
                if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                    entry.State = EntityState.Detached;
            }
            return true;
        }
    }

    public IQueryable<UserConnectionDto> QueryFriends(Guid userId)
    {
        // Two filtered halves joined to Users via inner-join — Users carries the soft-delete
        // global filter, so deleted accounts disappear without an explicit null check. Profile +
        // BackgroundCheck are left-joined (the sub-aggregates are created lazily, so absent rows
        // are normal — they just project null/None). Union dedupes by row contents, but
        // RequesterId/AddresseeId pairs are unique so dedupe is moot.
        var asRequester =
            from friendship in dbContext.Friendships.AsNoTracking()
            join other in dbContext.Users.AsNoTracking() on friendship.AddresseeId equals other.Id
            where friendship.Status == FriendshipStatus.Accepted && friendship.RequesterId == userId
            select new UserConnectionDto
            {
                Id = other.Id,
                Handle = dbContext.UserProfiles.Where(p => p.UserId == other.Id).Select(p => p.Handle).FirstOrDefault(),
                Nickname = dbContext.UserProfiles.Where(p => p.UserId == other.Id).Select(p => p.Nickname).FirstOrDefault(),
                DisplayName = dbContext.UserProfiles.Where(p => p.UserId == other.Id).Select(p => p.DisplayName).FirstOrDefault(),
                ImageUrl = dbContext.UserProfiles.Where(p => p.UserId == other.Id).Select(p => p.ImageUrl).FirstOrDefault(),
                BackgroundCheckBadge = dbContext.UserBackgroundChecks.Where(bc => bc.UserId == other.Id).Select(bc => bc.Badge).FirstOrDefault(),
                UserCreatedAtUtc = other.CreatedAtUtc,
                ConnectionCreatedAtUtc = friendship.CreatedAtUtc
            };

        var asAddressee =
            from friendship in dbContext.Friendships.AsNoTracking()
            join other in dbContext.Users.AsNoTracking() on friendship.RequesterId equals other.Id
            where friendship.Status == FriendshipStatus.Accepted && friendship.AddresseeId == userId
            select new UserConnectionDto
            {
                Id = other.Id,
                Handle = dbContext.UserProfiles.Where(p => p.UserId == other.Id).Select(p => p.Handle).FirstOrDefault(),
                Nickname = dbContext.UserProfiles.Where(p => p.UserId == other.Id).Select(p => p.Nickname).FirstOrDefault(),
                DisplayName = dbContext.UserProfiles.Where(p => p.UserId == other.Id).Select(p => p.DisplayName).FirstOrDefault(),
                ImageUrl = dbContext.UserProfiles.Where(p => p.UserId == other.Id).Select(p => p.ImageUrl).FirstOrDefault(),
                BackgroundCheckBadge = dbContext.UserBackgroundChecks.Where(bc => bc.UserId == other.Id).Select(bc => bc.Badge).FirstOrDefault(),
                UserCreatedAtUtc = other.CreatedAtUtc,
                ConnectionCreatedAtUtc = friendship.CreatedAtUtc
            };

        return asRequester.Union(asAddressee)
            .OrderByDescending(connection => connection.ConnectionCreatedAtUtc)
            .ThenBy(connection => connection.Id);
    }

    public IQueryable<UserConnectionDto> QueryIncomingFriendRequests(Guid userId)
    {
        return from friendship in dbContext.Friendships.AsNoTracking()
               join requester in dbContext.Users.AsNoTracking() on friendship.RequesterId equals requester.Id
               where friendship.Status == FriendshipStatus.Pending && friendship.AddresseeId == userId
               orderby friendship.CreatedAtUtc descending, requester.Id
               select new UserConnectionDto
               {
                   Id = requester.Id,
                   Handle = dbContext.UserProfiles.Where(p => p.UserId == requester.Id).Select(p => p.Handle).FirstOrDefault(),
                   Nickname = dbContext.UserProfiles.Where(p => p.UserId == requester.Id).Select(p => p.Nickname).FirstOrDefault(),
                   DisplayName = dbContext.UserProfiles.Where(p => p.UserId == requester.Id).Select(p => p.DisplayName).FirstOrDefault(),
                   ImageUrl = dbContext.UserProfiles.Where(p => p.UserId == requester.Id).Select(p => p.ImageUrl).FirstOrDefault(),
                   BackgroundCheckBadge = dbContext.UserBackgroundChecks.Where(bc => bc.UserId == requester.Id).Select(bc => bc.Badge).FirstOrDefault(),
                   UserCreatedAtUtc = requester.CreatedAtUtc,
                   ConnectionCreatedAtUtc = friendship.CreatedAtUtc
               };
    }

    public IQueryable<UserConnectionDto> QueryOutgoingFriendRequests(Guid userId)
    {
        return from friendship in dbContext.Friendships.AsNoTracking()
               join addressee in dbContext.Users.AsNoTracking() on friendship.AddresseeId equals addressee.Id
               where friendship.Status == FriendshipStatus.Pending && friendship.RequesterId == userId
               orderby friendship.CreatedAtUtc descending, addressee.Id
               select new UserConnectionDto
               {
                   Id = addressee.Id,
                   Handle = dbContext.UserProfiles.Where(p => p.UserId == addressee.Id).Select(p => p.Handle).FirstOrDefault(),
                   Nickname = dbContext.UserProfiles.Where(p => p.UserId == addressee.Id).Select(p => p.Nickname).FirstOrDefault(),
                   DisplayName = dbContext.UserProfiles.Where(p => p.UserId == addressee.Id).Select(p => p.DisplayName).FirstOrDefault(),
                   ImageUrl = dbContext.UserProfiles.Where(p => p.UserId == addressee.Id).Select(p => p.ImageUrl).FirstOrDefault(),
                   BackgroundCheckBadge = dbContext.UserBackgroundChecks.Where(bc => bc.UserId == addressee.Id).Select(bc => bc.Badge).FirstOrDefault(),
                   UserCreatedAtUtc = addressee.CreatedAtUtc,
                   ConnectionCreatedAtUtc = friendship.CreatedAtUtc
               };
    }

    public IQueryable<UserConnectionDto> QueryBlockedUsers(Guid userId)
    {
        return from block in dbContext.UserBlocks.AsNoTracking()
               join blocked in dbContext.Users.AsNoTracking() on block.BlockedId equals blocked.Id
               where block.BlockerId == userId
               orderby block.CreatedAtUtc descending, blocked.Id
               select new UserConnectionDto
               {
                   Id = blocked.Id,
                   Handle = dbContext.UserProfiles.Where(p => p.UserId == blocked.Id).Select(p => p.Handle).FirstOrDefault(),
                   Nickname = dbContext.UserProfiles.Where(p => p.UserId == blocked.Id).Select(p => p.Nickname).FirstOrDefault(),
                   DisplayName = dbContext.UserProfiles.Where(p => p.UserId == blocked.Id).Select(p => p.DisplayName).FirstOrDefault(),
                   ImageUrl = dbContext.UserProfiles.Where(p => p.UserId == blocked.Id).Select(p => p.ImageUrl).FirstOrDefault(),
                   BackgroundCheckBadge = dbContext.UserBackgroundChecks.Where(bc => bc.UserId == blocked.Id).Select(bc => bc.Badge).FirstOrDefault(),
                   UserCreatedAtUtc = blocked.CreatedAtUtc,
                   ConnectionCreatedAtUtc = block.CreatedAtUtc
               };
    }

    public Task<int> GetFlagCountAsync(Guid flaggedUserId, CancellationToken cancellationToken)
    {
        return dbContext.UserFlags
            .AsNoTracking()
            .CountAsync(flag => flag.FlaggedUserId == flaggedUserId, cancellationToken);
    }
}
