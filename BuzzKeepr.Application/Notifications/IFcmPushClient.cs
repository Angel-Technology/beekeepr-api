namespace BuzzKeepr.Application.Notifications;

// FCM equivalent of IApnsPushClient. Same shape on purpose — the notifier fans out
// per-platform via a stable receipt type so the prune/logging path is uniform.
public interface IFcmPushClient
{
    Task<IReadOnlyList<PushReceipt>> SendAsync(
        IReadOnlyList<string> deviceTokens,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? data,
        CancellationToken cancellationToken);
}
