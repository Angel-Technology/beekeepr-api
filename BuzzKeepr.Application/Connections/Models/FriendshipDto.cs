using BuzzKeepr.Domain.Enums;

namespace BuzzKeepr.Application.Connections.Models;

public sealed class FriendshipDto
{
    public Guid Id { get; init; }

    public Guid RequesterId { get; init; }

    public Guid AddresseeId { get; init; }

    public FriendshipStatus Status { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime? RespondedAtUtc { get; init; }
}
