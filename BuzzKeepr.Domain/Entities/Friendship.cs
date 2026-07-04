using BuzzKeepr.Domain.Enums;

namespace BuzzKeepr.Domain.Entities;

public sealed class Friendship
{
    public Guid Id { get; set; }

    public Guid RequesterId { get; set; }

    public User? Requester { get; set; }

    public Guid AddresseeId { get; set; }

    public User? Addressee { get; set; }

    public FriendshipStatus Status { get; set; } = FriendshipStatus.Pending;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? RespondedAtUtc { get; set; }
}
