using BuzzKeepr.Domain.Enums;

namespace BuzzKeepr.Application.Connections.Models;

// Public-profile projection used by friend lists, pending-request lists, and block lists.
// Mirrors UserSearchResultDto so the frontend can render the same card shape in every list.
//
// Contact fields are gated server-side by ContactVisibility + viewer-friendship status, same
// as on UserSearchResultDto. Even on the friends list we still evaluate the gate per row — if
// a friend later flips their ContactVisibility to Private, their contact fields go null.
public sealed class UserConnectionDto
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

    public string? GoogleVoicePhone { get; init; }

    public string? WhatsAppPhone { get; init; }

    public string? InstagramHandle { get; init; }

    public string? TelegramHandle { get; init; }

    public string? SnapchatHandle { get; init; }

    public string? SignalPhone { get; init; }

    public DateTime UserCreatedAtUtc { get; init; }

    // Timestamp of the relationship row (friendship/block) — frontends sort lists by this so the
    // most recent connection action appears first.
    public DateTime ConnectionCreatedAtUtc { get; init; }
}
