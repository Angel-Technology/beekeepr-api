namespace BuzzKeepr.Domain.Entities;

public sealed class UserFlag
{
    public Guid Id { get; set; }

    public Guid FlaggerId { get; set; }

    public User? Flagger { get; set; }

    public Guid FlaggedUserId { get; set; }

    public User? FlaggedUser { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
