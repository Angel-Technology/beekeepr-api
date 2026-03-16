using BuzzKeepr.Domain.Enums;

namespace BuzzKeepr.Domain.Entities;

public sealed class VerificationToken
{
    public Guid Id { get; set; }

    public Guid? UserId { get; set; }

    public string Email { get; set; } = string.Empty;

    public VerificationTokenPurpose Purpose { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public int FailedAttempts { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? ConsumedAtUtc { get; set; }

    public User? User { get; set; }
}
