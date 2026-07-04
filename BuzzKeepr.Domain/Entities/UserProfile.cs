using BuzzKeepr.Domain.Enums;

namespace BuzzKeepr.Domain.Entities;

// Self-reported profile data. Every field is user-supplied — never sourced from third-party
// identity providers, Persona inquiry data, or any other system-derived state. This is the
// App Store compliance boundary: anything stored here was typed in by the user themselves.
public sealed class UserProfile
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public User? User { get; set; }

    public string? DisplayName { get; set; }

    public string? Nickname { get; set; }

    public string? Handle { get; set; }

    public string? ImageUrl { get; set; }

    public ProfileVisibility ProfileVisibility { get; set; } = ProfileVisibility.Public;

    // Contact channels — all optional, user-supplied, and only shown to other members per
    // ContactVisibility. Phone is stored separately from the messaging-specific phones because
    // Checkr uses PhoneNumber as the criminal-check input; users may not want to share their
    // Checkr phone publicly.
    public string? PhoneNumber { get; set; }

    public string? GoogleVoicePhone { get; set; }

    public string? WhatsAppPhone { get; set; }

    public string? InstagramHandle { get; set; }

    public string? TelegramHandle { get; set; }

    public string? SnapchatHandle { get; set; }

    public string? SignalPhone { get; set; }

    public ContactVisibility ContactVisibility { get; set; } = ContactVisibility.Private;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAtUtc { get; set; }
}
