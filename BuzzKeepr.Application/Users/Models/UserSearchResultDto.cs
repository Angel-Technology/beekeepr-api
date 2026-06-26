using BuzzKeepr.Domain.Enums;

namespace BuzzKeepr.Application.Users.Models;

// Public-profile projection returned by searchUsers. Includes self-supplied profile fields plus
// the verification badge / timestamps. Persona-verified PII (verifiedFirstName/Last/Middle/etc.),
// email, subscription state, and internal identity-verification IDs are intentionally excluded
// — those are compliance-internal and never exposed to other users.
//
// Contact fields (phone numbers + social handles) are gated server-side by the row user's
// ContactVisibility setting: Public → emitted, ConnectionsOnly → emitted only if viewer is an
// accepted friend, Private → always null. Frontends should treat null contact fields as
// "not available to you," not as "the user didn't fill it in."
public sealed class UserSearchResultDto
{
    public Guid Id { get; init; }

    public string? Handle { get; init; }

    public string? Nickname { get; init; }

    public string? DisplayName { get; init; }

    public string? ImageUrl { get; init; }

    public BackgroundCheckBadge BackgroundCheckBadge { get; init; } = BackgroundCheckBadge.None;

    public DateTime? BackgroundCheckBadgeExpiresAtUtc { get; init; }

    public DateTime? CheckrLastCheckAtUtc { get; init; }

    public ProfileVisibility ProfileVisibility { get; init; } = ProfileVisibility.Public;

    public ContactVisibility ContactVisibility { get; init; } = ContactVisibility.Private;

    public string? PhoneNumber { get; init; }

    public string? GoogleVoicePhone { get; init; }

    public string? WhatsAppPhone { get; init; }

    public string? InstagramHandle { get; init; }

    public string? TelegramHandle { get; init; }

    public string? SignalPhone { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    // Drives the frontend's "Add / Pending / Accept / Friends" button on each row. Defaults to
    // None for anonymous searches (no viewer to compare against).
    public ViewerFriendshipState ViewerFriendshipState { get; init; } = ViewerFriendshipState.None;
}
