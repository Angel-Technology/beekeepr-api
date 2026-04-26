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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<User>(builder =>
        {
            builder.HasKey(user => user.Id);

            builder.Property(user => user.Email)
                .HasMaxLength(320)
                .IsRequired();

            builder.Property(user => user.DisplayName)
                .HasMaxLength(200);

            builder.Property(user => user.ImageUrl)
                .HasMaxLength(2048);

            builder.Property(user => user.IdentityVerificationStatus)
                .HasConversion<string>()
                .HasMaxLength(50)
                .HasDefaultValue(BuzzKeepr.Domain.Enums.IdentityVerificationStatus.NotStarted)
                .IsRequired();

            builder.Property(user => user.PersonaInquiryId)
                .HasMaxLength(100);

            builder.Property(user => user.PersonaInquiryStatus)
                .HasConversion<string>()
                .HasMaxLength(50);

            builder.Property(user => user.PersonaInquiryUpdatedAtUtc)
                .HasColumnType("timestamp with time zone");

            builder.Property(user => user.VerifiedFirstName)
                .HasMaxLength(200);

            builder.Property(user => user.VerifiedMiddleName)
                .HasMaxLength(200);

            builder.Property(user => user.VerifiedLastName)
                .HasMaxLength(200);

            builder.Property(user => user.VerifiedBirthdate)
                .HasMaxLength(20);

            builder.Property(user => user.VerifiedLicenseState)
                .HasMaxLength(10);

            builder.Property(user => user.PhoneNumber)
                .HasMaxLength(32);

            builder.Property(user => user.CheckrProfileId)
                .HasMaxLength(64);

            builder.Property(user => user.CheckrLastCheckId)
                .HasMaxLength(64);

            builder.Property(user => user.CheckrLastCheckAtUtc)
                .HasColumnType("timestamp with time zone");

            builder.Property(user => user.BackgroundCheckBadge)
                .HasConversion<string>()
                .HasMaxLength(50)
                .HasDefaultValue(BackgroundCheckBadge.None)
                .IsRequired();

            builder.Property(user => user.BackgroundCheckBadgeExpiresAtUtc)
                .HasColumnType("timestamp with time zone");

            builder.Property(user => user.TermsAcceptedAtUtc)
                .HasColumnType("timestamp with time zone");

            builder.Property(user => user.WelcomeEmailSentAtUtc)
                .HasColumnType("timestamp with time zone");

            builder.Property(user => user.SubscriptionStatus)
                .HasConversion<string>()
                .HasMaxLength(50)
                .HasDefaultValue(SubscriptionStatus.None)
                .IsRequired();

            builder.Property(user => user.SubscriptionEntitlement)
                .HasMaxLength(100);

            builder.Property(user => user.SubscriptionProductId)
                .HasMaxLength(200);

            builder.Property(user => user.SubscriptionStore)
                .HasConversion<string>()
                .HasMaxLength(50);

            builder.Property(user => user.SubscriptionCurrentPeriodEndUtc)
                .HasColumnType("timestamp with time zone");

            builder.Property(user => user.SubscriptionUpdatedAtUtc)
                .HasColumnType("timestamp with time zone");

            builder.Property(user => user.RevenueCatAppUserId)
                .HasMaxLength(200);

            builder.HasIndex(user => user.Email)
                .IsUnique();

            builder.HasIndex(user => user.PersonaInquiryId)
                .IsUnique();

            builder.HasIndex(user => user.CheckrProfileId)
                .IsUnique();

            builder.HasIndex(user => user.RevenueCatAppUserId)
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

            builder.HasIndex(token => token.TokenHash)
                .IsUnique();

            builder.HasIndex(token => new { token.Email, token.Purpose, token.ExpiresAtUtc });
        });
    }
}
