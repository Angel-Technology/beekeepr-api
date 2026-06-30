namespace BuzzKeepr.Application.Notifications.Models;

public sealed class RegisterPushTokenResult
{
    public bool TokenRequired { get; init; }

    public bool Success { get; init; }
}
