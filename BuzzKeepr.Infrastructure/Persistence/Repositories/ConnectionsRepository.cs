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

    // Visibility model summary used by all four list projections below:
    //   - viewerIsFriend is a compile-time constant per query (true on QueryFriends, false on
    //     the other three) so SQL gets a simple `WHEN contact_visibility = 'ConnectionsOnly'
    //     AND <viewerIsFriend> THEN x ELSE NULL` per contact field — no EXISTS subquery needed
    //     for the friendship check.
    //   - After PR 3 the `Public` ContactVisibility was removed, so non-friend lists
    //     (Incoming/Outgoing/Blocked) always project null for the 6 contact fields. The CASE
    //     simplifies to a constant null on those queries.
    //   - profile/bc are LEFT-joined; a missing row produces null, which the projection collapses
    //     to default values (None badge, Public profile, Private contact, null contact fields).
    //
    // Each projection is inlined rather than extracted into a helper because EF Core can't
    // translate method calls inside an IQueryable expression tree — the `new UserConnectionDto {
    // ... }` literal must be visible to the translator.

    public IQueryable<UserConnectionDto> QueryFriends(Guid userId)
    {
        // Friends list: every row is an Accepted friendship, so ConnectionsOnly contact emits;
        // Private still hides (strict gating). With Public removed in PR 3, the only opt-in is
        // ConnectionsOnly — so the test `!= Private` reads as "user has opted to share with
        // friends." Equivalent to `== ConnectionsOnly` but symmetric with the gating intent.
        var asRequester =
            from friendship in dbContext.Friendships.AsNoTracking()
            join other in dbContext.Users.AsNoTracking() on friendship.AddresseeId equals other.Id
            join profileJoin in dbContext.UserProfiles.AsNoTracking() on other.Id equals profileJoin.UserId into profileLeft
            from profile in profileLeft.DefaultIfEmpty()
            join bcJoin in dbContext.UserBackgroundChecks.AsNoTracking() on other.Id equals bcJoin.UserId into bcLeft
            from bc in bcLeft.DefaultIfEmpty()
            where friendship.Status == FriendshipStatus.Accepted && friendship.RequesterId == userId
            select new UserConnectionDto
            {
                Id = other.Id,
                Handle = profile != null ? profile.Handle : null,
                Nickname = profile != null ? profile.Nickname : null,
                DisplayName = profile != null ? profile.DisplayName : null,
                ImageUrl = profile != null ? profile.ImageUrl : null,
                BackgroundCheckBadge = bc != null ? bc.Badge : BackgroundCheckBadge.None,
                BackgroundCheckBadgeExpiresAtUtc = bc != null ? bc.BadgeExpiresAtUtc : null,
                CheckrLastCheckAtUtc = bc != null ? bc.CheckrLastCheckAtUtc : null,
                ProfileVisibility = profile != null ? profile.ProfileVisibility : ProfileVisibility.Public,
                ContactVisibility = profile != null ? profile.ContactVisibility : ContactVisibility.Private,
                GoogleVoicePhone = profile != null && profile.ContactVisibility != ContactVisibility.Private ? profile.GoogleVoicePhone : null,
                WhatsAppPhone = profile != null && profile.ContactVisibility != ContactVisibility.Private ? profile.WhatsAppPhone : null,
                InstagramHandle = profile != null && profile.ContactVisibility != ContactVisibility.Private ? profile.InstagramHandle : null,
                TelegramHandle = profile != null && profile.ContactVisibility != ContactVisibility.Private ? profile.TelegramHandle : null,
                SnapchatHandle = profile != null && profile.ContactVisibility != ContactVisibility.Private ? profile.SnapchatHandle : null,
                SignalPhone = profile != null && profile.ContactVisibility != ContactVisibility.Private ? profile.SignalPhone : null,
                UserCreatedAtUtc = other.CreatedAtUtc,
                ConnectionCreatedAtUtc = friendship.CreatedAtUtc
            };

        var asAddressee =
            from friendship in dbContext.Friendships.AsNoTracking()
            join other in dbContext.Users.AsNoTracking() on friendship.RequesterId equals other.Id
            join profileJoin in dbContext.UserProfiles.AsNoTracking() on other.Id equals profileJoin.UserId into profileLeft
            from profile in profileLeft.DefaultIfEmpty()
            join bcJoin in dbContext.UserBackgroundChecks.AsNoTracking() on other.Id equals bcJoin.UserId into bcLeft
            from bc in bcLeft.DefaultIfEmpty()
            where friendship.Status == FriendshipStatus.Accepted && friendship.AddresseeId == userId
            select new UserConnectionDto
            {
                Id = other.Id,
                Handle = profile != null ? profile.Handle : null,
                Nickname = profile != null ? profile.Nickname : null,
                DisplayName = profile != null ? profile.DisplayName : null,
                ImageUrl = profile != null ? profile.ImageUrl : null,
                BackgroundCheckBadge = bc != null ? bc.Badge : BackgroundCheckBadge.None,
                BackgroundCheckBadgeExpiresAtUtc = bc != null ? bc.BadgeExpiresAtUtc : null,
                CheckrLastCheckAtUtc = bc != null ? bc.CheckrLastCheckAtUtc : null,
                ProfileVisibility = profile != null ? profile.ProfileVisibility : ProfileVisibility.Public,
                ContactVisibility = profile != null ? profile.ContactVisibility : ContactVisibility.Private,
                GoogleVoicePhone = profile != null && profile.ContactVisibility != ContactVisibility.Private ? profile.GoogleVoicePhone : null,
                WhatsAppPhone = profile != null && profile.ContactVisibility != ContactVisibility.Private ? profile.WhatsAppPhone : null,
                InstagramHandle = profile != null && profile.ContactVisibility != ContactVisibility.Private ? profile.InstagramHandle : null,
                TelegramHandle = profile != null && profile.ContactVisibility != ContactVisibility.Private ? profile.TelegramHandle : null,
                SnapchatHandle = profile != null && profile.ContactVisibility != ContactVisibility.Private ? profile.SnapchatHandle : null,
                SignalPhone = profile != null && profile.ContactVisibility != ContactVisibility.Private ? profile.SignalPhone : null,
                UserCreatedAtUtc = other.CreatedAtUtc,
                ConnectionCreatedAtUtc = friendship.CreatedAtUtc
            };

        return asRequester.Union(asAddressee)
            .OrderByDescending(connection => connection.ConnectionCreatedAtUtc)
            .ThenBy(connection => connection.Id);
    }

    public IQueryable<UserConnectionDto> QueryIncomingFriendRequests(Guid userId)
    {
        // Pending requests aren't friendships, and Public was removed — so the 6 contact fields
        // always project null on this list. ConnectionsOnly users only share with friends.
        return from friendship in dbContext.Friendships.AsNoTracking()
               join requester in dbContext.Users.AsNoTracking() on friendship.RequesterId equals requester.Id
               join profileJoin in dbContext.UserProfiles.AsNoTracking() on requester.Id equals profileJoin.UserId into profileLeft
               from profile in profileLeft.DefaultIfEmpty()
               join bcJoin in dbContext.UserBackgroundChecks.AsNoTracking() on requester.Id equals bcJoin.UserId into bcLeft
               from bc in bcLeft.DefaultIfEmpty()
               where friendship.Status == FriendshipStatus.Pending && friendship.AddresseeId == userId
               orderby friendship.CreatedAtUtc descending, requester.Id
               select new UserConnectionDto
               {
                   Id = requester.Id,
                   Handle = profile != null ? profile.Handle : null,
                   Nickname = profile != null ? profile.Nickname : null,
                   DisplayName = profile != null ? profile.DisplayName : null,
                   ImageUrl = profile != null ? profile.ImageUrl : null,
                   BackgroundCheckBadge = bc != null ? bc.Badge : BackgroundCheckBadge.None,
                   BackgroundCheckBadgeExpiresAtUtc = bc != null ? bc.BadgeExpiresAtUtc : null,
                   CheckrLastCheckAtUtc = bc != null ? bc.CheckrLastCheckAtUtc : null,
                   ProfileVisibility = profile != null ? profile.ProfileVisibility : ProfileVisibility.Public,
                   ContactVisibility = profile != null ? profile.ContactVisibility : ContactVisibility.Private,
                   GoogleVoicePhone = null,
                   WhatsAppPhone = null,
                   InstagramHandle = null,
                   TelegramHandle = null,
                   SnapchatHandle = null,
                   SignalPhone = null,
                   UserCreatedAtUtc = requester.CreatedAtUtc,
                   ConnectionCreatedAtUtc = friendship.CreatedAtUtc
               };
    }

    public IQueryable<UserConnectionDto> QueryOutgoingFriendRequests(Guid userId)
    {
        // Same as Incoming — no friendship yet, no Public option, so contact fields always null.
        return from friendship in dbContext.Friendships.AsNoTracking()
               join addressee in dbContext.Users.AsNoTracking() on friendship.AddresseeId equals addressee.Id
               join profileJoin in dbContext.UserProfiles.AsNoTracking() on addressee.Id equals profileJoin.UserId into profileLeft
               from profile in profileLeft.DefaultIfEmpty()
               join bcJoin in dbContext.UserBackgroundChecks.AsNoTracking() on addressee.Id equals bcJoin.UserId into bcLeft
               from bc in bcLeft.DefaultIfEmpty()
               where friendship.Status == FriendshipStatus.Pending && friendship.RequesterId == userId
               orderby friendship.CreatedAtUtc descending, addressee.Id
               select new UserConnectionDto
               {
                   Id = addressee.Id,
                   Handle = profile != null ? profile.Handle : null,
                   Nickname = profile != null ? profile.Nickname : null,
                   DisplayName = profile != null ? profile.DisplayName : null,
                   ImageUrl = profile != null ? profile.ImageUrl : null,
                   BackgroundCheckBadge = bc != null ? bc.Badge : BackgroundCheckBadge.None,
                   BackgroundCheckBadgeExpiresAtUtc = bc != null ? bc.BadgeExpiresAtUtc : null,
                   CheckrLastCheckAtUtc = bc != null ? bc.CheckrLastCheckAtUtc : null,
                   ProfileVisibility = profile != null ? profile.ProfileVisibility : ProfileVisibility.Public,
                   ContactVisibility = profile != null ? profile.ContactVisibility : ContactVisibility.Private,
                   GoogleVoicePhone = null,
                   WhatsAppPhone = null,
                   InstagramHandle = null,
                   TelegramHandle = null,
                   SnapchatHandle = null,
                   SignalPhone = null,
                   UserCreatedAtUtc = addressee.CreatedAtUtc,
                   ConnectionCreatedAtUtc = friendship.CreatedAtUtc
               };
    }

    public IQueryable<UserConnectionDto> QueryBlockedUsers(Guid userId)
    {
        // Block removes any friendship, so the viewer is never a friend of anyone on this list.
        // With Public removed, ConnectionsOnly contact never emits to non-friends — so contact
        // fields are always null on the blocked list.
        return from block in dbContext.UserBlocks.AsNoTracking()
               join blocked in dbContext.Users.AsNoTracking() on block.BlockedId equals blocked.Id
               join profileJoin in dbContext.UserProfiles.AsNoTracking() on blocked.Id equals profileJoin.UserId into profileLeft
               from profile in profileLeft.DefaultIfEmpty()
               join bcJoin in dbContext.UserBackgroundChecks.AsNoTracking() on blocked.Id equals bcJoin.UserId into bcLeft
               from bc in bcLeft.DefaultIfEmpty()
               where block.BlockerId == userId
               orderby block.CreatedAtUtc descending, blocked.Id
               select new UserConnectionDto
               {
                   Id = blocked.Id,
                   Handle = profile != null ? profile.Handle : null,
                   Nickname = profile != null ? profile.Nickname : null,
                   DisplayName = profile != null ? profile.DisplayName : null,
                   ImageUrl = profile != null ? profile.ImageUrl : null,
                   BackgroundCheckBadge = bc != null ? bc.Badge : BackgroundCheckBadge.None,
                   BackgroundCheckBadgeExpiresAtUtc = bc != null ? bc.BadgeExpiresAtUtc : null,
                   CheckrLastCheckAtUtc = bc != null ? bc.CheckrLastCheckAtUtc : null,
                   ProfileVisibility = profile != null ? profile.ProfileVisibility : ProfileVisibility.Public,
                   ContactVisibility = profile != null ? profile.ContactVisibility : ContactVisibility.Private,
                   GoogleVoicePhone = null,
                   WhatsAppPhone = null,
                   InstagramHandle = null,
                   TelegramHandle = null,
                   SnapchatHandle = null,
                   SignalPhone = null,
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
