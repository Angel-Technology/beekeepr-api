namespace BuzzKeepr.Application.Notifications.Models;

public sealed class UnregisterPushTokenResult
{
    public bool TokenRequired { get; init; }

    public bool Success { get; init; }
}
