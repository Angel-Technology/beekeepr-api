using BuzzKeepr.Application.Users;
using BuzzKeepr.Application.Users.Models;
using BuzzKeepr.Domain.Entities;
using BuzzKeepr.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace BuzzKeepr.Infrastructure.Persistence.Repositories;

public sealed class UserRepository(BuzzKeeprDbContext dbContext) : IUserRepository
{
    // Trigram similarity below this is dropped from results — empirically ~0.3 catches obvious typos
    // ("samul" -> "samuel") without flooding results with unrelated names. Tune if needed.
    private const double TrigramSimilarityFloor = 0.3;

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.Users
            .AsNoTracking()
            .Include(user => user.Profile)
            .Include(user => user.IdentityVerification)
            .Include(user => user.BackgroundCheck)
            .Include(user => user.Subscription)
            .FirstOrDefaultAsync(user => user.Id == id, cancellationToken);
    }

    public async Task<User?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.Users
            .Include(user => user.Profile)
            .Include(user => user.IdentityVerification)
            .Include(user => user.BackgroundCheck)
            .Include(user => user.Subscription)
            .FirstOrDefaultAsync(user => user.Id == id, cancellationToken);
    }

    public async Task<User?> GetByIdForUpdateIncludingDeletedAsync(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.Users
            .IgnoreQueryFilters()
            .Include(user => user.Profile)
            .Include(user => user.IdentityVerification)
            .Include(user => user.BackgroundCheck)
            .Include(user => user.Subscription)
            .FirstOrDefaultAsync(user => user.Id == id, cancellationToken);
    }

    public async Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters: emails of pending-deletion users are still in the unique index;
        // pretending they don't exist would cause an INSERT to fail at the DB layer.
        return await dbContext.Users
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(user => user.Email == email, cancellationToken);
    }

    public async Task<bool> HandleExistsAsync(string handle, Guid? excludeUserId, CancellationToken cancellationToken)
    {
        // Handle now lives on UserProfile; the unique index travelled with it.
        return await dbContext.UserProfiles
            .AsNoTracking()
            .AnyAsync(profile => profile.Handle == handle
                && (excludeUserId == null || profile.UserId != excludeUserId), cancellationToken);
    }

    public IQueryable<UserSearchResultDto> Search(string normalizedQuery, Guid? excludeUserId)
    {
        var prefixPattern = normalizedQuery + "%";

        // Search now drives off UserProfiles — that's where Handle/Nickname/DisplayName/ImageUrl
        // live. Joining Users (which carries the soft-delete query filter) excludes profiles
        // belonging to soft-deleted accounts without an explicit nav check.
        var profilesWithUsers =
            from profile in dbContext.UserProfiles.AsNoTracking()
            join user in dbContext.Users.AsNoTracking() on profile.UserId equals user.Id
            select new { Profile = profile, User = user };

        return profilesWithUsers
            .Where(row => excludeUserId == null || row.User.Id != excludeUserId)
            // ProfileVisibility=Private opts the user out of community discovery entirely. They
            // can still be friended directly if someone already knows the handle, but they don't
            // appear in search.
            .Where(row => row.Profile.ProfileVisibility == ProfileVisibility.Public)
            // Hide users involved in a block in either direction with the viewer. Flag implies block
            // (see ConnectionsService.FlagUserAsync), so flagged users also drop out here.
            .Where(row => excludeUserId == null
                || !dbContext.UserBlocks.Any(block =>
                    (block.BlockerId == excludeUserId && block.BlockedId == row.User.Id)
                    || (block.BlockerId == row.User.Id && block.BlockedId == excludeUserId)))
            .Where(row =>
                (row.Profile.Handle != null && (row.Profile.Handle == normalizedQuery || EF.Functions.ILike(row.Profile.Handle, prefixPattern)))
                || (row.Profile.Nickname != null && EF.Functions.TrigramsSimilarity(row.Profile.Nickname, normalizedQuery) >= TrigramSimilarityFloor)
                || (row.Profile.DisplayName != null && EF.Functions.TrigramsSimilarity(row.Profile.DisplayName, normalizedQuery) >= TrigramSimilarityFloor))
            .OrderBy(row =>
                row.Profile.Handle == normalizedQuery ? 0 :
                row.Profile.Handle != null && EF.Functions.ILike(row.Profile.Handle, prefixPattern) ? 1 :
                2)
            .ThenByDescending(row =>
                (row.Profile.Nickname != null ? EF.Functions.TrigramsSimilarity(row.Profile.Nickname, normalizedQuery) : 0d)
                + (row.Profile.DisplayName != null ? EF.Functions.TrigramsSimilarity(row.Profile.DisplayName, normalizedQuery) : 0d))
            .ThenBy(row => row.User.Id) // stable tiebreaker so cursor pagination doesn't skip/duplicate
            .Select(row => new UserSearchResultDto
            {
                Id = row.User.Id,
                Handle = row.Profile.Handle,
                Nickname = row.Profile.Nickname,
                DisplayName = row.Profile.DisplayName,
                ImageUrl = row.Profile.ImageUrl,
                BackgroundCheckBadge = dbContext.UserBackgroundChecks
                    .Where(bc => bc.UserId == row.User.Id)
                    .Select(bc => bc.Badge)
                    .FirstOrDefault(),
                BackgroundCheckBadgeExpiresAtUtc = dbContext.UserBackgroundChecks
                    .Where(bc => bc.UserId == row.User.Id)
                    .Select(bc => bc.BadgeExpiresAtUtc)
                    .FirstOrDefault(),
                CheckrLastCheckAtUtc = dbContext.UserBackgroundChecks
                    .Where(bc => bc.UserId == row.User.Id)
                    .Select(bc => bc.CheckrLastCheckAtUtc)
                    .FirstOrDefault(),
                ProfileVisibility = row.Profile.ProfileVisibility,
                ContactVisibility = row.Profile.ContactVisibility,
                // Contact fields are visible only when ContactVisibility == ConnectionsOnly AND
                // the viewer is an accepted friend. Public was removed (PR 3) — there's no
                // longer a way to share contact info with strangers. Each field re-checks
                // independently; EF translates to a CASE per field, and the friend-EXISTS
                // subquery hits the (RequesterId,Status)/(AddresseeId,Status) indexes so each
                // evaluation is cheap.
                GoogleVoicePhone = row.Profile.ContactVisibility == ContactVisibility.ConnectionsOnly
                    && excludeUserId != null
                    && dbContext.Friendships.Any(f => f.Status == FriendshipStatus.Accepted
                        && ((f.RequesterId == excludeUserId && f.AddresseeId == row.User.Id)
                            || (f.RequesterId == row.User.Id && f.AddresseeId == excludeUserId)))
                    ? row.Profile.GoogleVoicePhone : null,
                WhatsAppPhone = row.Profile.ContactVisibility == ContactVisibility.ConnectionsOnly
                    && excludeUserId != null
                    && dbContext.Friendships.Any(f => f.Status == FriendshipStatus.Accepted
                        && ((f.RequesterId == excludeUserId && f.AddresseeId == row.User.Id)
                            || (f.RequesterId == row.User.Id && f.AddresseeId == excludeUserId)))
                    ? row.Profile.WhatsAppPhone : null,
                InstagramHandle = row.Profile.ContactVisibility == ContactVisibility.ConnectionsOnly
                    && excludeUserId != null
                    && dbContext.Friendships.Any(f => f.Status == FriendshipStatus.Accepted
                        && ((f.RequesterId == excludeUserId && f.AddresseeId == row.User.Id)
                            || (f.RequesterId == row.User.Id && f.AddresseeId == excludeUserId)))
                    ? row.Profile.InstagramHandle : null,
                TelegramHandle = row.Profile.ContactVisibility == ContactVisibility.ConnectionsOnly
                    && excludeUserId != null
                    && dbContext.Friendships.Any(f => f.Status == FriendshipStatus.Accepted
                        && ((f.RequesterId == excludeUserId && f.AddresseeId == row.User.Id)
                            || (f.RequesterId == row.User.Id && f.AddresseeId == excludeUserId)))
                    ? row.Profile.TelegramHandle : null,
                SnapchatHandle = row.Profile.ContactVisibility == ContactVisibility.ConnectionsOnly
                    && excludeUserId != null
                    && dbContext.Friendships.Any(f => f.Status == FriendshipStatus.Accepted
                        && ((f.RequesterId == excludeUserId && f.AddresseeId == row.User.Id)
                            || (f.RequesterId == row.User.Id && f.AddresseeId == excludeUserId)))
                    ? row.Profile.SnapchatHandle : null,
                SignalPhone = row.Profile.ContactVisibility == ContactVisibility.ConnectionsOnly
                    && excludeUserId != null
                    && dbContext.Friendships.Any(f => f.Status == FriendshipStatus.Accepted
                        && ((f.RequesterId == excludeUserId && f.AddresseeId == row.User.Id)
                            || (f.RequesterId == row.User.Id && f.AddresseeId == excludeUserId)))
                    ? row.Profile.SignalPhone : null,
                CreatedAtUtc = row.User.CreatedAtUtc,
                // Three EXISTS subqueries — each hits an indexed (RequesterId,Status) /
                // (AddresseeId,Status) lookup so the per-row cost is small at typeahead page sizes.
                ViewerFriendshipState = excludeUserId == null
                    ? ViewerFriendshipState.None
                    : dbContext.Friendships.Any(friendship =>
                        ((friendship.RequesterId == excludeUserId && friendship.AddresseeId == row.User.Id)
                         || (friendship.RequesterId == row.User.Id && friendship.AddresseeId == excludeUserId))
                        && friendship.Status == FriendshipStatus.Accepted)
                        ? ViewerFriendshipState.Friends
                        : dbContext.Friendships.Any(friendship =>
                            friendship.RequesterId == excludeUserId
                            && friendship.AddresseeId == row.User.Id
                            && friendship.Status == FriendshipStatus.Pending)
                            ? ViewerFriendshipState.RequestSent
                            : dbContext.Friendships.Any(friendship =>
                                friendship.RequesterId == row.User.Id
                                && friendship.AddresseeId == excludeUserId
                                && friendship.Status == FriendshipStatus.Pending)
                                ? ViewerFriendshipState.RequestReceived
                                : ViewerFriendshipState.None,
            });
    }

    public async Task AddAsync(User user, CancellationToken cancellationToken)
    {
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
