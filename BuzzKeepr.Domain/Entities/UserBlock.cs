namespace BuzzKeepr.Domain.Entities;

public sealed class UserBlock
{
    public Guid Id { get; set; }

    public Guid BlockerId { get; set; }

    public User? Blocker { get; set; }

    public Guid BlockedId { get; set; }

    public User? Blocked { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
