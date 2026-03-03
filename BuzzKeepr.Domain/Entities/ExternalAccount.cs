using BuzzKeepr.Domain.Enums;

namespace BuzzKeepr.Domain.Entities;

public sealed class ExternalAccount
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public AuthProvider Provider { get; set; }

    public string ProviderAccountId { get; set; } = string.Empty;

    public string? ProviderEmail { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? LastSignInAtUtc { get; set; }

    public User User { get; set; } = null!;
}