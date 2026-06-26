using BuzzKeepr.Domain.Enums;

namespace BuzzKeepr.Application.Users.Models;

// All fields nullable — each represents an *optional* update. A null value means "don't touch
// this field"; an empty string means "clear this field." UserService.UpdateProfileAsync
// distinguishes the two.
public sealed class UpdateProfileInput
{
    public string? Nickname { get; init; }

    public string? Handle { get; init; }

    public string? DisplayName { get; init; }

    public string? ImageUrl { get; init; }

    public string? PhoneNumber { get; init; }

    public string? GoogleVoicePhone { get; init; }

    public string? WhatsAppPhone { get; init; }

    public string? InstagramHandle { get; init; }

    public string? TelegramHandle { get; init; }

    public string? SignalPhone { get; init; }

    public ProfileVisibility? ProfileVisibility { get; init; }

    public ContactVisibility? ContactVisibility { get; init; }
}
