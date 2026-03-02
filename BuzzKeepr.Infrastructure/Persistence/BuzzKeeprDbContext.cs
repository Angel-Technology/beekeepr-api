using BuzzKeepr.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BuzzKeepr.Infrastructure.Persistence;

public sealed class BuzzKeeprDbContext(DbContextOptions<BuzzKeeprDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<ExternalAccount> ExternalAccounts => Set<ExternalAccount>();

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
                .HasMaxLength(50)
                .IsRequired();

            builder.Property(account => account.ProviderAccountId)
                .HasMaxLength(200)
                .IsRequired();

            builder.HasOne(account => account.User)
                .WithMany(user => user.ExternalAccounts)
                .HasForeignKey(account => account.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(account => new { account.Provider, account.ProviderAccountId })
                .IsUnique();
        });
    }
}
