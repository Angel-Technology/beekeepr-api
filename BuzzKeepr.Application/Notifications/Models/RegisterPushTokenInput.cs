using BuzzKeepr.Domain.Enums;

namespace BuzzKeepr.Application.Notifications.Models;

public sealed class RegisterPushTokenInput
{
    public string Token { get; init; } = string.Empty;

    public PushPlatform Platform { get; init; }
}
