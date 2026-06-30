using BuzzKeepr.Application.Notifications;
using Microsoft.EntityFrameworkCore;

namespace BuzzKeepr.Infrastructure.Persistence.Repositories;

public sealed class NotificationProfileLookup(BuzzKeeprDbContext dbContext) : INotificationProfileLookup
{
    public async Task<string?> GetDisplayNameAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Single indexed lookup on UserProfiles.UserId (unique index). Falls back through the
        // display-name → nickname → handle chain; returns null only if the user has no profile
        // row at all (signed up but never completed profile). Notifier substitutes "Someone."
        var profile = await dbContext.UserProfiles
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new { p.DisplayName, p.Nickname, p.Handle })
            .FirstOrDefaultAsync(cancellationToken);

        if (profile is null)
            return null;

        return profile.DisplayName
            ?? profile.Nickname
            ?? profile.Handle;
    }
}
