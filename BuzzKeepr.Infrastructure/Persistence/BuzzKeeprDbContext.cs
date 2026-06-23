using BuzzKeepr.Domain.Entities;
using BuzzKeepr.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace BuzzKeepr.Infrastructure.Persistence;

public sealed class BuzzKeeprDbContext(DbContextOptions<BuzzKeeprDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<ExternalAccount> ExternalAccounts => Set<ExternalAccount>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<VerificationToken> VerificationTokens => Set<VerificationToken>();
    public DbSet<PromoCode> PromoCodes => Set<PromoCode>();
    public DbSet<PromoRedemption> PromoRedemptions => Set<PromoRedemption>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<UserBlock> UserBlocks => Set<UserBlock>();
    public DbSet<UserFlag> UserFlags => Set<UserFlag>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<UserIdentityVerification> UserIdentityVerifications => Set<UserIdentityVerification>();
    public DbSet<UserBackgroundCheck> UserBackgroundChecks => Set<UserBackgroundCheck>();
    public DbSet<UserSubscription> UserSubscriptions => Set<UserSubscription>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<User>(builder =>
        {
            builder.HasKey(user => user.Id);

            builder.Property(user => user.Email)
                .HasMaxLength(320)
                .IsRequired();

            builder.Property(user => user.TermsAcceptedAtUtc)
                .HasColumnType("timestamp with time zone");

            builder.Property(user => user.WelcomeEmailSentAtUtc)
                .HasColumnType("timestamp with time zone");

            builder.Property(user => user.DeletedAtUtc)
                .HasColumnType("timestamp with time zone");

            builder.HasQueryFilter(user => user.DeletedAtUtc == null);

            builder.HasIndex(user => user.Email)
                .IsUnique();
        });

        modelBuilder.Entity<UserProfile>(builder =>
        {
            builder.HasKey(profile => profile.Id);

            // Postgres-side Guid generation. The Ensure*() helpers leave Id at Guid.Empty so EF's
            // state-inference treats nav-attached new rows as Added; the DB fills in the actual key.
            builder.Property(profile => profile.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .ValueGeneratedOnAdd();

            builder.Property(profile => profile.DisplayName)
                .HasMaxLength(200);

            builder.Property(profile => profile.Nickname)
                .HasMaxLength(50);

            builder.Property(profile => profile.Handle)
                .HasMaxLength(20);

            builder.Property(profile => profile.ImageUrl)
                .HasMaxLength(2048);

            builder.Property(profile => profile.PhoneNumber)
                .HasMaxLength(32);

            builder.Property(profile => profile.CreatedAtUtc)
                .HasColumnType("timestamp with time zone");

            builder.Property(profile => profile.UpdatedAtUtc)
                .HasColumnType("timestamp with time zone");

            builder.HasOne(profile => profile.User)
                .WithOne(user => user.Profile)
                .HasForeignKey<UserProfile>(profile => profile.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(profile => profile.UserId)
                .IsUnique();

            // Handle uniqueness moved off Users along with the column itself.
            builder.HasIndex(profile => profile.Handle)
                .IsUnique();
        });

        modelBuilder.Entity<UserIdentityVerification>(builder =>
        {
            builder.HasKey(iv => iv.Id);

            builder.Property(iv => iv.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .ValueGeneratedOnAdd();

            builder.Property(iv => iv.Status)
                .HasConversion<string>()
                .HasMaxLength(50)
                .HasDefaultValue(IdentityVerificationStatus.NotStarted)
                .IsRequired();

            builder.Property(iv => iv.PersonaInquiryId)
                .HasMaxLength(100);

            builder.Property(iv => iv.PersonaInquiryStatus)
                .HasConversion<string>()
                .HasMaxLength(50);

            builder.Property(iv => iv.PersonaInquiryUpdatedAtUtc)
                .HasColumnType("timestamp with time zone");

            builder.Property(iv => iv.VerifiedFirstName)
                .HasMaxLength(200);

            builder.Property(iv => iv.VerifiedMiddleName)
                .HasMaxLength(200);

            builder.Property(iv => iv.VerifiedLastName)
                .HasMaxLength(200);

            builder.Property(iv => iv.VerifiedBirthdate)
                .HasMaxLength(20);

            builder.Property(iv => iv.VerifiedLicenseState)
                .HasMaxLength(10);

            builder.Property(iv => iv.PersonaVerifiedAtUtc)
                .HasColumnType("timestamp with time zone");

            builder.HasOne(iv => iv.User)
                .WithOne(user => user.IdentityVerification)
                .HasForeignKey<UserIdentityVerification>(iv => iv.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(iv => iv.UserId)
                .IsUnique();

            builder.HasIndex(iv => iv.PersonaInquiryId)
                .IsUnique();
        });

        modelBuilder.Entity<UserBackgroundCheck>(builder =>
        {
            builder.HasKey(bc => bc.Id);

            builder.Property(bc => bc.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .ValueGeneratedOnAdd();

            builder.Property(bc => bc.CheckrProfileId)
                .HasMaxLength(64);

            builder.Property(bc => bc.CheckrLastCheckId)
                .HasMaxLength(64);

            builder.Property(bc => bc.CheckrLastCheckAtUtc)
                .HasColumnType("timestamp with time zone");

            builder.Property(bc => bc.Badge)
                .HasConversion<string>()
                .HasMaxLength(50)
                .HasDefaultValue(BackgroundCheckBadge.None)
                .IsRequired();

            builder.Property(bc => bc.BadgeExpiresAtUtc)
                .HasColumnType("timestamp with time zone");

            builder.HasOne(bc => bc.User)
                .WithOne(user => user.BackgroundCheck)
                .HasForeignKey<UserBackgroundCheck>(bc => bc.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(bc => bc.UserId)
                .IsUnique();

            builder.HasIndex(bc => bc.CheckrProfileId)
                .IsUnique();
        });

        modelBuilder.Entity<UserSubscription>(builder =>
        {
            builder.HasKey(sub => sub.Id);

            builder.Property(sub => sub.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .ValueGeneratedOnAdd();

            builder.Property(sub => sub.Status)
                .HasConversion<string>()
                .HasMaxLength(50)
                .HasDefaultValue(SubscriptionStatus.None)
                .IsRequired();

            builder.Property(sub => sub.Entitlement)
                .HasMaxLength(100);

            builder.Property(sub => sub.ProductId)
                .HasMaxLength(200);

            builder.Property(sub => sub.Store)
                .HasConversion<string>()
                .HasMaxLength(50);

            builder.Property(sub => sub.CurrentPeriodEndUtc)
                .HasColumnType("timestamp with time zone");

            builder.Property(sub => sub.UpdatedAtUtc)
                .HasColumnType("timestamp with time zone");

            builder.Property(sub => sub.RevenueCatAppUserId)
                .HasMaxLength(200);

            builder.HasOne(sub => sub.User)
                .WithOne(user => user.Subscription)
                .HasForeignKey<UserSubscription>(sub => sub.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(sub => sub.UserId)
                .IsUnique();

            builder.HasIndex(sub => sub.RevenueCatAppUserId)
                .IsUnique();
        });

        modelBuilder.Entity<ExternalAccount>(builder =>
        {
            builder.HasKey(account => account.Id);

            builder.Property(account => account.Provider)
                .HasConversion<string>()
                .HasMaxLength(50)
                .IsRequired();

            builder.Property(account => account.ProviderAccountId)
                .HasMaxLength(200)
                .IsRequired();

            builder.Property(account => account.ProviderEmail)
                .HasMaxLength(320);

            builder.HasOne(account => account.User)
                .WithMany(user => user.ExternalAccounts)
                .HasForeignKey(account => account.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(account => new { account.Provider, account.ProviderAccountId })
                .IsUnique();
        });

        modelBuilder.Entity<Session>(builder =>
        {
            builder.HasKey(session => session.Id);

            builder.Property(session => session.TokenHash)
                .HasMaxLength(128)
                .IsRequired();

            builder.Property(session => session.IpAddress)
                .HasMaxLength(64);

            builder.Property(session => session.UserAgent)
                .HasMaxLength(512);

            builder.HasOne(session => session.User)
                .WithMany(user => user.Sessions)
                .HasForeignKey(session => session.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(session => session.TokenHash)
                .IsUnique();
        });

        modelBuilder.Entity<VerificationToken>(builder =>
        {
            builder.HasKey(token => token.Id);

            builder.Property(token => token.Email)
                .HasMaxLength(320)
                .IsRequired();

            builder.Property(token => token.Purpose)
                .HasConversion<string>()
                .HasMaxLength(50)
                .IsRequired();

            builder.Property(token => token.TokenHash)
                .HasMaxLength(128)
                .IsRequired();

            builder.Property(token => token.FailedAttempts)
                .HasDefaultValue(0)
                .IsRequired();

            builder.HasOne(token => token.User)
                .WithMany(user => user.VerificationTokens)
                .HasForeignKey(token => token.UserId)
                .OnDelete(DeleteBehavior.SetNull);

            // Partial: uniqueness only needs to hold among lookup-able rows (the verify path
            // filters ConsumedAtUtc IS NULL). Without the filter, re-issuing a sign-in code
            // whose hash matches an already-consumed row 23505s — e.g. review-account static
            // PINs always hash to the same value.
            builder.HasIndex(token => token.TokenHash)
                .IsUnique()
                .HasFilter("\"ConsumedAtUtc\" IS NULL");

            builder.HasIndex(token => new { token.Email, token.Purpose, token.ExpiresAtUtc });
        });

        modelBuilder.Entity<PromoCode>(builder =>
        {
            builder.HasKey(promo => promo.Id);

            builder.Property(promo => promo.Code)
                .HasMaxLength(64)
                .IsRequired();

            builder.Property(promo => promo.EntitlementId)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(promo => promo.Duration)
                .HasConversion<string>()
                .HasMaxLength(50)
                .IsRequired();

            builder.Property(promo => promo.RedemptionsUsed)
                .HasDefaultValue(0)
                .IsRequired();

            builder.Property(promo => promo.IsActive)
                .HasDefaultValue(true)
                .IsRequired();

            builder.Property(promo => promo.ExpiresAtUtc)
                .HasColumnType("timestamp with time zone");

            builder.Property(promo => promo.CreatedAtUtc)
                .HasColumnType("timestamp with time zone");

            builder.HasIndex(promo => promo.Code)
                .IsUnique();
        });

        modelBuilder.Entity<PromoRedemption>(builder =>
        {
            builder.HasKey(redemption => redemption.Id);

            builder.Property(redemption => redemption.RedeemedAtUtc)
                .HasColumnType("timestamp with time zone");

            builder.HasOne(redemption => redemption.PromoCode)
                .WithMany(promo => promo.Redemptions)
                .HasForeignKey(redemption => redemption.PromoCodeId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(redemption => redemption.User)
                .WithMany()
                .HasForeignKey(redemption => redemption.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // The unique index is what enforces "each user can only redeem a given code once" —
            // the repository relies on catching its violation as the AlreadyRedeemed signal.
            builder.HasIndex(redemption => new { redemption.PromoCodeId, redemption.UserId })
                .IsUnique();
        });

        modelBuilder.Entity<Friendship>(builder =>
        {
            builder.HasKey(friendship => friendship.Id);

            builder.Property(friendship => friendship.Status)
                .HasConversion<string>()
                .HasMaxLength(50)
                .HasDefaultValue(FriendshipStatus.Pending)
                .IsRequired();

            builder.Property(friendship => friendship.CreatedAtUtc)
                .HasColumnType("timestamp with time zone");

            builder.Property(friendship => friendship.RespondedAtUtc)
                .HasColumnType("timestamp with time zone");

            // Cascade on hard-delete (the 72-hour grace purge) — soft-deleted users keep their
            // friendship rows so a cancelled deletion restores the social graph intact.
            builder.HasOne(friendship => friendship.Requester)
                .WithMany()
                .HasForeignKey(friendship => friendship.RequesterId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(friendship => friendship.Addressee)
                .WithMany()
                .HasForeignKey(friendship => friendship.AddresseeId)
                .OnDelete(DeleteBehavior.Cascade);

            // Prevents duplicate same-direction requests (A→B inserted twice). Cross-direction
            // races (A→B and B→A racing) are handled in ConnectionsService by the FindFriendshipBetween
            // lookup; if both sides hit at once, one becomes the row and the other auto-accepts.
            builder.HasIndex(friendship => new { friendship.RequesterId, friendship.AddresseeId })
                .IsUnique();

            // Supports "list my outgoing pending requests" without scanning the full table.
            builder.HasIndex(friendship => new { friendship.RequesterId, friendship.Status });

            // Supports "list my incoming pending requests" and "list my friends from the addressee side".
            builder.HasIndex(friendship => new { friendship.AddresseeId, friendship.Status });
        });

        modelBuilder.Entity<UserBlock>(builder =>
        {
            builder.HasKey(block => block.Id);

            builder.Property(block => block.CreatedAtUtc)
                .HasColumnType("timestamp with time zone");

            builder.HasOne(block => block.Blocker)
                .WithMany()
                .HasForeignKey(block => block.BlockerId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(block => block.Blocked)
                .WithMany()
                .HasForeignKey(block => block.BlockedId)
                // Restrict on the blocked side — Cascade on both FKs causes Postgres to reject
                // "multiple cascade paths" through the same User row.
                .OnDelete(DeleteBehavior.Restrict);

            // One block row per (blocker, blocked) pair. The repository catches violations as the
            // AlreadyBlocked signal.
            builder.HasIndex(block => new { block.BlockerId, block.BlockedId })
                .IsUnique();

            // Reverse lookup: "has anyone blocked me?" — needed when validating an incoming friend
            // request to make sure the target hasn't blocked the caller.
            builder.HasIndex(block => block.BlockedId);
        });

        modelBuilder.Entity<UserFlag>(builder =>
        {
            builder.HasKey(flag => flag.Id);

            builder.Property(flag => flag.CreatedAtUtc)
                .HasColumnType("timestamp with time zone");

            builder.HasOne(flag => flag.Flagger)
                .WithMany()
                .HasForeignKey(flag => flag.FlaggerId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(flag => flag.FlaggedUser)
                .WithMany()
                .HasForeignKey(flag => flag.FlaggedUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Distinct-flagger uniqueness — the flag count is COUNT(*) per FlaggedUserId, so the
            // same user re-flagging is a no-op rather than incrementing the count.
            builder.HasIndex(flag => new { flag.FlaggerId, flag.FlaggedUserId })
                .IsUnique();

            // Lookup for moderation: count distinct flaggers per target.
            builder.HasIndex(flag => flag.FlaggedUserId);
        });
    }
}
