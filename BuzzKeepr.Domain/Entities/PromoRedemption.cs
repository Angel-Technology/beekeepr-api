namespace BuzzKeepr.Domain.Entities;

public sealed class PromoRedemption
{
    public Guid Id { get; set; }

    public Guid PromoCodeId { get; set; }

    public PromoCode? PromoCode { get; set; }

    public Guid UserId { get; set; }

    public User? User { get; set; }

    public DateTime RedeemedAtUtc { get; set; } = DateTime.UtcNow;
}
