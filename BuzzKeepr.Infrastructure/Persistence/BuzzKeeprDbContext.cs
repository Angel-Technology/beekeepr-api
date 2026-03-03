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

            builder.HasIndex(user => user.Email)
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
