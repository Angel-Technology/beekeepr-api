using BuzzKeepr.Application.Notifications.Models;

namespace BuzzKeepr.Application.Notifications;

public interface IPushTokenService
{
    Task<RegisterPushTokenResult> RegisterAsync(Guid userId, RegisterPushTokenInput input, CancellationToken cancellationToken);

    Task<UnregisterPushTokenResult> UnregisterAsync(Guid userId, string token, CancellationToken cancellationToken);
}
