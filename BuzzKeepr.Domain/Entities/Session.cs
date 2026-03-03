namespace BuzzKeepr.Domain.Entities;

public sealed class Session
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? LastSeenAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public User User { get; set; } = null!;
}