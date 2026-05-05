using BuzzKeepr.Application.IdentityVerification.Models;
using BuzzKeepr.Domain.Entities;
using BuzzKeepr.Domain.Enums;
using BuzzKeepr.Infrastructure.IdentityVerification;
using BuzzKeepr.Infrastructure.Persistence;
using BuzzKeepr.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BuzzKeepr.IntegrationTests.IdentityVerification;

[Collection(IntegrationTestCollection.Name)]
public sealed class BackgroundCheckRenewalTests(PostgresFixture postgres) : IAsyncLifetime
{
    // We invoke the sweep on demand rather than waiting on the 6-hour schedule.
    // Reflection: SweepOnceAsync is private; we use the BackgroundService's ExecuteAsync
    // by calling the internal sweep method via reflection so each test can run a single pass
    // and assert deterministically.
    private readonly BuzzKeeprApiFactory factory = new(postgres);

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync()
    {
        factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Sweep_RenewsExpiredBadge_WhenSubscriptionIsActive()
    {
        var user = await SeedUserAsync(
            badge: BackgroundCheckBadge.Approved,
            badgeExpiry: DateTime.UtcNow.AddMinutes(-5),
            subscriptionStatus: SubscriptionStatus.Active,
            subscriptionPeriodEnd: DateTime.UtcNow.AddDays(20));

        factory.FakeCheckrTrust.NextResult = new CreateInstantCriminalCheckResult
        {
            Success = true,
            CheckId = "chk_renewed",
            ProfileId = "prf_seeded",
            ResultCount = 0,
            HasPossibleMatches = false
        };

        await SweepOnceAsync();

        Assert.Single(factory.FakeCheckrTrust.Calls);
        var call = factory.FakeCheckrTrust.Calls[0];
        Assert.Equal("prf_seeded", call.ProfileId); // profile-reuse path

        var stored = await ReloadAsync(user.Id);
        Assert.Equal(BackgroundCheckBadge.Approved, stored.BackgroundCheckBadge);
        Assert.NotNull(stored.BackgroundCheckBadgeExpiresAtUtc);
        Assert.True(stored.BackgroundCheckBadgeExpiresAtUtc > DateTime.UtcNow.AddMonths(2),
            "badge should have a fresh ~3-month expiry");
        Assert.Equal("chk_renewed", stored.CheckrLastCheckId);
    }

    [Fact]
    public async Task Sweep_FlipsBadgeToDenied_WhenRenewalFindsRecords()
    {
        var user = await SeedUserAsync(
            badge: BackgroundCheckBadge.Approved,
            badgeExpiry: DateTime.UtcNow.AddMinutes(-1),
            subscriptionStatus: SubscriptionStatus.Active,
            subscriptionPeriodEnd: DateTime.UtcNow.AddDays(15));

        factory.FakeCheckrTrust.NextResult = new CreateInstantCriminalCheckResult
        {
            Success = true,
            CheckId = "chk_denied",
            ProfileId = "prf_seeded",
            ResultCount = 1,
            HasPossibleMatches = true
        };

        await SweepOnceAsync();

        var stored = await ReloadAsync(user.Id);
        Assert.Equal(BackgroundCheckBadge.Denied, stored.BackgroundCheckBadge);
        Assert.Equal(true, stored.CheckrLastCheckHasPossibleMatches);
    }

    [Fact]
    public async Task Sweep_SkipsUsers_WhenSubscriptionIsExpired()
    {
        await SeedUserAsync(
            badge: BackgroundCheckBadge.Approved,
            badgeExpiry: DateTime.UtcNow.AddMinutes(-30),
            subscriptionStatus: SubscriptionStatus.Expired,
            subscriptionPeriodEnd: DateTime.UtcNow.AddDays(-1));

        await SweepOnceAsync();

        Assert.Empty(factory.FakeCheckrTrust.Calls);
    }

    [Fact]
    public async Task Sweep_SkipsUsers_WhenSubscriptionIsNone()
    {
        await SeedUserAsync(
            badge: BackgroundCheckBadge.Approved,
            badgeExpiry: DateTime.UtcNow.AddMinutes(-30),
            subscriptionStatus: SubscriptionStatus.None,
            subscriptionPeriodEnd: null);

        await SweepOnceAsync();

        Assert.Empty(factory.FakeCheckrTrust.Calls);
    }

    [Fact]
    public async Task Sweep_SkipsUsers_WhenBadgeStillValid()
    {
        await SeedUserAsync(
            badge: BackgroundCheckBadge.Approved,
            badgeExpiry: DateTime.UtcNow.AddDays(20),
            subscriptionStatus: SubscriptionStatus.Active,
            subscriptionPeriodEnd: DateTime.UtcNow.AddDays(20));

        await SweepOnceAsync();

        Assert.Empty(factory.FakeCheckrTrust.Calls);
    }

    [Fact]
    public async Task Sweep_HandlesCancelledButStillInPeriod_AsActive()
    {
        // After CANCELLATION the user keeps premium until current_period_end. We should still
        // renew their badge during this window — they paid for it.
        var user = await SeedUserAsync(
            badge: BackgroundCheckBadge.Approved,
            badgeExpiry: DateTime.UtcNow.AddMinutes(-5),
            subscriptionStatus: SubscriptionStatus.Cancelled,
            subscriptionPeriodEnd: DateTime.UtcNow.AddDays(10));

        factory.FakeCheckrTrust.NextResult = new CreateInstantCriminalCheckResult
        {
            Success = true,
            CheckId = "chk_cancel_grace",
            ProfileId = "prf_seeded",
            ResultCount = 0,
            HasPossibleMatches = false
        };

        await SweepOnceAsync();

        Assert.Single(factory.FakeCheckrTrust.Calls);
        var stored = await ReloadAsync(user.Id);
        Assert.Equal(BackgroundCheckBadge.Approved, stored.BackgroundCheckBadge);
    }

    private async Task SweepOnceAsync()
    {
        // We construct a fresh sweeper rather than reaching into the registered IHostedService —
        // the registered one auto-runs on host startup and could race with our test seed. Building
        // a separate instance with the host's IServiceScopeFactory is deterministic. SweepOnceAsync
        // is private, so reflection — keeping the method private in production and not exposing a
        // public hook just for tests is the right tradeoff.
        var scopeFactory = factory.Services.GetRequiredService<IServiceScopeFactory>();
        var sweeper = new BackgroundCheckRenewalBackgroundService(
            scopeFactory,
            NullLogger<BackgroundCheckRenewalBackgroundService>.Instance);

        var method = typeof(BackgroundCheckRenewalBackgroundService)
            .GetMethod("SweepOnceAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("SweepOnceAsync not found");

        await (Task)method.Invoke(sweeper, [CancellationToken.None])!;
    }

    private async Task<User> SeedUserAsync(
        BackgroundCheckBadge badge,
        DateTime badgeExpiry,
        SubscriptionStatus subscriptionStatus,
        DateTime? subscriptionPeriodEnd)
    {
        var email = $"renewal-{Guid.NewGuid():N}@buzzkeepr.test";
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            EmailVerified = true,
            CreatedAtUtc = DateTime.UtcNow.AddMonths(-3),
            VerifiedFirstName = "Renew",
            VerifiedLastName = "Tester",
            CheckrProfileId = "prf_seeded",
            CheckrLastCheckId = "chk_seeded",
            CheckrLastCheckAtUtc = DateTime.UtcNow.AddMonths(-3),
            CheckrLastCheckHasPossibleMatches = false,
            BackgroundCheckBadge = badge,
            BackgroundCheckBadgeExpiresAtUtc = badgeExpiry,
            SubscriptionStatus = subscriptionStatus,
            SubscriptionEntitlement = subscriptionStatus == SubscriptionStatus.None ? null : "premium",
            SubscriptionProductId = subscriptionStatus == SubscriptionStatus.None ? null : "premium_monthly",
            SubscriptionStore = subscriptionStatus == SubscriptionStatus.None ? null : SubscriptionStore.AppStore,
            SubscriptionCurrentPeriodEndUtc = subscriptionPeriodEnd,
            SubscriptionWillRenew = subscriptionStatus == SubscriptionStatus.Active
        };

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        return user;
    }

    private async Task<User> ReloadAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        return await dbContext.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
    }
}
