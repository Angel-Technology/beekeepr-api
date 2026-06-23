namespace BuzzKeepr.Domain.Entities;

// Self-reported profile data. Every field is user-supplied — never sourced from third-party
// identity providers, Persona inquiry data, or any other system-derived state. This is the
// App Store compliance boundary: anything stored here was typed in by the user themselves.
public sealed class UserProfile
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public User? User { get; set; }

    public string? DisplayName { get; set; }

    public string? Nickname { get; set; }

    public string? Handle { get; set; }

    public string? ImageUrl { get; set; }

    public string? PhoneNumber { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAtUtc { get; set; }
}
