using BuzzKeepr.Domain.Enums;

namespace BuzzKeepr.Application.Connections.Models;

// Public-profile projection used by friend lists, pending-request lists, and block lists.
// Mirrors UserSearchResultDto so the frontend can render the same card shape in every list.
public sealed class UserConnectionDto
{
    public Guid Id { get; init; }

    public string? Handle { get; init; }

    public string? Nickname { get; init; }

    public string? DisplayName { get; init; }

    public string? ImageUrl { get; init; }

    public BackgroundCheckBadge BackgroundCheckBadge { get; init; } = BackgroundCheckBadge.None;

    public DateTime UserCreatedAtUtc { get; init; }

    // Timestamp of the relationship row (friendship/block) — frontends sort lists by this so the
    // most recent connection action appears first.
    public DateTime ConnectionCreatedAtUtc { get; init; }
}
