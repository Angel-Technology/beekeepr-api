using BuzzKeepr.Application.Connections.Models;
using BuzzKeepr.Domain.Enums;

namespace BuzzKeepr.API.GraphQL.Types;

public sealed class FriendshipGraph
{
    public static FriendshipGraph From(FriendshipDto dto) => new()
    {
        Id = dto.Id,
        RequesterId = dto.RequesterId,
        AddresseeId = dto.AddresseeId,
        Status = dto.Status,
        CreatedAtUtc = dto.CreatedAtUtc,
        RespondedAtUtc = dto.RespondedAtUtc
    };

    public Guid Id { get; init; }

    public Guid RequesterId { get; init; }

    public Guid AddresseeId { get; init; }

    public FriendshipStatus Status { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime? RespondedAtUtc { get; init; }
}
