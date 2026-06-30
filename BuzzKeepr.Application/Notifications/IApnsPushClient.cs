namespace BuzzKeepr.Application.Notifications;

public interface IApnsPushClient
{
    // Sends one logical notification to one or many APNs device tokens. APNs HTTP/2 doesn't
    // batch, so this fans out N POSTs concurrently and aggregates the receipts. Per-token
    // receipts let the notifier act on errors — most notably pruning rows when APNs returns
    // 410 Gone or BadDeviceToken (the token is dead, won't ever work again).
    Task<IReadOnlyList<PushReceipt>> SendAsync(
        IReadOnlyList<string> deviceTokens,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? data,
        CancellationToken cancellationToken);
}

public sealed class PushReceipt
{
    public string Token { get; init; } = string.Empty;

    public bool Ok { get; init; }

    // APNs error reason code, e.g. "Unregistered", "BadDeviceToken", "ExpiredProviderToken".
    // The notifier prunes the token row when this is one of the dead-token codes.
    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }
}
