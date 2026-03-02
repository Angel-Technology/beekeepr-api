namespace BuzzKeepr.Domain.Entities;

public sealed class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public bool EmailVerified { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<ExternalAccount> ExternalAccounts { get; set; } = new List<ExternalAccount>();
}
