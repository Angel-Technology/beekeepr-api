using BuzzKeepr.Application.Connections.Models;
using BuzzKeepr.Domain.Enums;

namespace BuzzKeepr.API.GraphQL.Types;

// Public-profile shape returned by friends/blocked/pending-request lists. Mirrors the projection
// behind searchUsers so the frontend can render the same row component everywhere.
public sealed class UserConnectionGraph
{
    public static UserConnectionGraph From(UserConnectionDto dto) => new()
    {
        Id = dto.Id,
        Handle = dto.Handle,
        Nickname = dto.Nickname,
        DisplayName = dto.DisplayName,
        ImageUrl = dto.ImageUrl,
        BackgroundCheckBadge = dto.BackgroundCheckBadge,
        UserCreatedAtUtc = dto.UserCreatedAtUtc,
        ConnectionCreatedAtUtc = dto.ConnectionCreatedAtUtc
    };

    public Guid Id { get; init; }

    public string? Handle { get; init; }

    public string? Nickname { get; init; }

    public string? DisplayName { get; init; }

    public string? ImageUrl { get; init; }

    public BackgroundCheckBadge BackgroundCheckBadge { get; init; } = BackgroundCheckBadge.None;

    public DateTime UserCreatedAtUtc { get; init; }

    public DateTime ConnectionCreatedAtUtc { get; init; }
}
