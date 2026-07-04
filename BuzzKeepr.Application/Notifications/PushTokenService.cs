using BuzzKeepr.Application.Notifications.Models;
using BuzzKeepr.Domain.Entities;

namespace BuzzKeepr.Application.Notifications;

public sealed class PushTokenService(IPushTokenRepository pushTokenRepository) : IPushTokenService
{
    public async Task<RegisterPushTokenResult> RegisterAsync(Guid userId, RegisterPushTokenInput input, CancellationToken cancellationToken)
    {
        var trimmed = (input.Token ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
            return new RegisterPushTokenResult { TokenRequired = true };

        // The token string is globally unique by Expo guarantee. If we already have a row for
        // it, the most useful behavior is:
        //   - Bump LastSeenAtUtc (proves the device is still active).
        //   - Re-bind UserId if a different user is signing in on this device — sign-out should
        //     have unregistered, but if it didn't (force-quit, bug, etc.), this prevents the
        //     previous user from getting notifications meant for the new one.
        var existing = await pushTokenRepository.FindByTokenAsync(trimmed, cancellationToken);
        if (existing is not null)
        {
            existing.UserId = userId;
            existing.Platform = input.Platform;
            existing.LastSeenAtUtc = DateTime.UtcNow;
        }
        else
        {
            pushTokenRepository.Add(new UserPushToken
            {
                UserId = userId,
                Token = trimmed,
                Platform = input.Platform,
                CreatedAtUtc = DateTime.UtcNow,
                LastSeenAtUtc = DateTime.UtcNow
            });
        }

        await pushTokenRepository.SaveChangesAsync(cancellationToken);

        return new RegisterPushTokenResult { Success = true };
    }

    public async Task<UnregisterPushTokenResult> UnregisterAsync(Guid userId, string token, CancellationToken cancellationToken)
    {
        var trimmed = (token ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
            return new UnregisterPushTokenResult { TokenRequired = true };

        var existing = await pushTokenRepository.FindByTokenAsync(trimmed, cancellationToken);
        if (existing is not null && existing.UserId == userId)
        {
            // Only delete if it actually belongs to the caller. A different user owning the
            // token means we hit the re-bind path on next register and shouldn't pre-empt that.
            pushTokenRepository.Remove(existing);
            await pushTokenRepository.SaveChangesAsync(cancellationToken);
        }

        // Idempotent: success even if the token didn't exist or belonged to someone else. The
        // frontend doesn't care; it just wants confirmation that nothing's listening anymore.
        return new UnregisterPushTokenResult { Success = true };
    }
}
