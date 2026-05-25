using BuzzKeepr.Domain.Enums;

namespace BuzzKeepr.Domain.Entities;

public sealed class PromoCode
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string EntitlementId { get; set; } = string.Empty;

    public PromoCodeDuration Duration { get; set; }

    public int? MaxRedemptions { get; set; }

    public int RedemptionsUsed { get; set; }

    public DateTime? ExpiresAtUtc { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<PromoRedemption> Redemptions { get; set; } = new List<PromoRedemption>();
}
