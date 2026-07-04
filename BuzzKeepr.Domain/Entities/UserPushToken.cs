using BuzzKeepr.Domain.Enums;

namespace BuzzKeepr.Domain.Entities;

// Expo push token issued by Notifications.getExpoPushTokenAsync() on the mobile client and
// registered with the backend on app launch. A user can have multiple tokens (iPhone + iPad,
// dev + prod builds, reinstall replaces old). The token string is globally unique by Expo
// guarantee, so we dedupe on it — re-registering an existing token just bumps LastSeenAtUtc.
//
// Stale tokens are pruned in two ways:
//   1. Frontend calls unregisterPushToken on sign-out so the next user on the same device
//      doesn't receive notifications meant for the previous user.
//   2. When Expo returns DeviceNotRegistered for a send, the notifier deletes that row —
//      the token died (uninstall, OS-level rotation, etc.) and won't ever work again.
public sealed class UserPushToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public User? User { get; set; }

    // The opaque ExponentPushToken[...] string. Globally unique per device per Expo install.
    public string Token { get; set; } = string.Empty;

    public PushPlatform Platform { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Bumped on every registerPushToken call. Lets a future sweeper prune tokens that haven't
    // checked in for N months — those phones are probably gone.
    public DateTime LastSeenAtUtc { get; set; } = DateTime.UtcNow;
}
