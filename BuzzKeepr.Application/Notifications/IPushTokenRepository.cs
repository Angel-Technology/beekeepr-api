using BuzzKeepr.Domain.Entities;

namespace BuzzKeepr.Application.Notifications;

public interface IPushTokenRepository
{
    // Returns the existing row if one matches the token string, else null.
    Task<UserPushToken?> FindByTokenAsync(string token, CancellationToken cancellationToken);

    // Every push token a user owns (across devices). The notifier joins on this when fanning
    // a notification out to all of a recipient's devices.
    Task<IReadOnlyList<UserPushToken>> GetTokensForUserAsync(Guid userId, CancellationToken cancellationToken);

    void Add(UserPushToken token);

    void Remove(UserPushToken token);

    // Bulk-delete by token string — used by the notifier when Expo reports DeviceNotRegistered.
    // Returns the number of rows actually deleted (0 if the token was already gone, e.g. due to
    // a concurrent sign-out unregister).
    Task<int> DeleteByTokensAsync(IReadOnlyList<string> tokens, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
