namespace BuzzKeepr.Domain.Entities;

// Lazy creation of sub-aggregate rows. Each helper returns the existing aggregate if present,
// otherwise constructs an empty one wired to the user via the navigation. EF Core picks the new
// entity up through nav fix-up and tracks it as Added on the next SaveChanges.
//
// Important: Id is intentionally left at Guid.Empty so EF Core's state-inference treats the
// new row as Added (rather than as an existing-but-modified row, which would generate an UPDATE
// against a non-existent row). The actual primary-key value is supplied by the DB-side
// gen_random_uuid() default configured in OnModelCreating.
public static class UserAggregateExtensions
{
    public static UserProfile EnsureProfile(this User user)
    {
        return user.Profile ??= new UserProfile
        {
            UserId = user.Id,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public static UserIdentityVerification EnsureIdentityVerification(this User user)
    {
        return user.IdentityVerification ??= new UserIdentityVerification
        {
            UserId = user.Id
        };
    }

    public static UserBackgroundCheck EnsureBackgroundCheck(this User user)
    {
        return user.BackgroundCheck ??= new UserBackgroundCheck
        {
            UserId = user.Id
        };
    }

    public static UserSubscription EnsureSubscription(this User user)
    {
        return user.Subscription ??= new UserSubscription
        {
            UserId = user.Id
        };
    }
}
