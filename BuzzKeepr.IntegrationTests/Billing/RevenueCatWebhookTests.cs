using System.Net;
using System.Text;
using System.Text.Json;
using BuzzKeepr.Application.Billing.Models;
using BuzzKeepr.Domain.Entities;
using BuzzKeepr.Domain.Enums;
using BuzzKeepr.Infrastructure.Persistence;
using BuzzKeepr.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BuzzKeepr.IntegrationTests.Billing;

[Collection(IntegrationTestCollection.Name)]
public sealed class RevenueCatWebhookTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly BuzzKeeprApiFactory factory = new(postgres);

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync()
    {
        factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task InitialPurchase_TrialPeriod_FlipsUserToTrialingWithEntitlementAndExpiry()
    {
        var user = await SeedUserAsync();
        var expiresAt = DateTime.UtcNow.AddDays(7);
        var body = BuildEvent(
            type: "INITIAL_PURCHASE",
            appUserId: user.Id.ToString(),
            productId: "premium_monthly",
            entitlementId: "premium",
            store: "APP_STORE",
            periodType: "TRIAL",
            expiresAt: expiresAt);

        var response = await PostWebhookAsync(body, BuzzKeeprApiFactory.RevenueCatWebhookToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var stored = await ReloadAsync(user.Id);
        Assert.Equal(SubscriptionStatus.Trialing, stored.SubscriptionStatus);
        Assert.Equal("premium", stored.SubscriptionEntitlement);
        Assert.Equal("premium_monthly", stored.SubscriptionProductId);
        Assert.Equal(SubscriptionStore.AppStore, stored.SubscriptionStore);
        Assert.Equal(true, stored.SubscriptionWillRenew);
        Assert.NotNull(stored.SubscriptionCurrentPeriodEndUtc);
        Assert.Equal(expiresAt, stored.SubscriptionCurrentPeriodEndUtc.Value, TimeSpan.FromSeconds(1));
        Assert.Equal(user.Id.ToString(), stored.RevenueCatAppUserId);
    }

    [Fact]
    public async Task Renewal_AfterTrial_FlipsToActiveAndExtendsExpiry()
    {
        var user = await SeedUserAsync();
        var initialExpiry = DateTime.UtcNow.AddDays(7);
        await PostWebhookAsync(BuildEvent(
            type: "INITIAL_PURCHASE",
            appUserId: user.Id.ToString(),
            productId: "premium_monthly",
            entitlementId: "premium",
            store: "APP_STORE",
            periodType: "TRIAL",
            expiresAt: initialExpiry,
            eventTimestamp: DateTime.UtcNow.AddMinutes(-2)),
            BuzzKeeprApiFactory.RevenueCatWebhookToken);

        var renewalExpiry = DateTime.UtcNow.AddDays(37);
        await PostWebhookAsync(BuildEvent(
            type: "RENEWAL",
            appUserId: user.Id.ToString(),
            productId: "premium_monthly",
            entitlementId: "premium",
            store: "APP_STORE",
            periodType: "NORMAL",
            expiresAt: renewalExpiry),
            BuzzKeeprApiFactory.RevenueCatWebhookToken);

        var stored = await ReloadAsync(user.Id);
        Assert.Equal(SubscriptionStatus.Active, stored.SubscriptionStatus);
        Assert.Equal(renewalExpiry, stored.SubscriptionCurrentPeriodEndUtc!.Value, TimeSpan.FromSeconds(1));
        Assert.Equal(true, stored.SubscriptionWillRenew);
    }

    [Fact]
    public async Task Cancellation_KeepsAccessUntilPeriodEndButFlipsWillRenewFalse()
    {
        var user = await SeedUserAsync();
        var purchaseTimestamp = DateTime.UtcNow.AddMinutes(-1);
        var expiresAt = DateTime.UtcNow.AddDays(20);
        await PostWebhookAsync(BuildEvent(
            type: "INITIAL_PURCHASE",
            appUserId: user.Id.ToString(),
            productId: "premium_monthly",
            entitlementId: "premium",
            store: "PLAY_STORE",
            periodType: "NORMAL",
            expiresAt: expiresAt,
            eventTimestamp: purchaseTimestamp),
            BuzzKeeprApiFactory.RevenueCatWebhookToken);

        await PostWebhookAsync(BuildEvent(
            type: "CANCELLATION",
            appUserId: user.Id.ToString(),
            productId: "premium_monthly",
            entitlementId: "premium",
            store: "PLAY_STORE",
            periodType: "NORMAL",
            expiresAt: expiresAt),
            BuzzKeeprApiFactory.RevenueCatWebhookToken);

        var stored = await ReloadAsync(user.Id);
        Assert.Equal(SubscriptionStatus.Cancelled, stored.SubscriptionStatus);
        Assert.Equal(false, stored.SubscriptionWillRenew);
        Assert.Equal(expiresAt, stored.SubscriptionCurrentPeriodEndUtc!.Value, TimeSpan.FromSeconds(1));
        Assert.True(SubscriptionDto.IsLocallyActive(stored), "Cancelled-but-not-yet-expired should still be locally active");
    }

    [Fact]
    public async Task Expiration_ClearsActiveStatus()
    {
        var user = await SeedUserAsync();
        await PostWebhookAsync(BuildEvent(
            type: "EXPIRATION",
            appUserId: user.Id.ToString(),
            productId: "premium_monthly",
            entitlementId: "premium",
            store: "APP_STORE",
            periodType: "NORMAL",
            expiresAt: DateTime.UtcNow.AddSeconds(-1)),
            BuzzKeeprApiFactory.RevenueCatWebhookToken);

        var stored = await ReloadAsync(user.Id);
        Assert.Equal(SubscriptionStatus.Expired, stored.SubscriptionStatus);
        Assert.Equal(false, stored.SubscriptionWillRenew);
        Assert.False(SubscriptionDto.IsLocallyActive(stored));
    }

    [Fact]
    public async Task StaleEvent_OlderThanWatermark_IsIgnored()
    {
        var user = await SeedUserAsync();
        var newerTimestamp = DateTime.UtcNow;
        var newerExpiry = DateTime.UtcNow.AddDays(30);
        await PostWebhookAsync(BuildEvent(
            type: "RENEWAL",
            appUserId: user.Id.ToString(),
            productId: "premium_monthly",
            entitlementId: "premium",
            store: "APP_STORE",
            periodType: "NORMAL",
            expiresAt: newerExpiry,
            eventTimestamp: newerTimestamp),
            BuzzKeeprApiFactory.RevenueCatWebhookToken);

        // Replay an older CANCELLATION that arrived late — should be dropped.
        var staleTimestamp = newerTimestamp.AddMinutes(-5);
        await PostWebhookAsync(BuildEvent(
            type: "CANCELLATION",
            appUserId: user.Id.ToString(),
            productId: "premium_monthly",
            entitlementId: "premium",
            store: "APP_STORE",
            periodType: "NORMAL",
            expiresAt: newerExpiry,
            eventTimestamp: staleTimestamp),
            BuzzKeeprApiFactory.RevenueCatWebhookToken);

        var stored = await ReloadAsync(user.Id);
        Assert.Equal(SubscriptionStatus.Active, stored.SubscriptionStatus);
        Assert.Equal(true, stored.SubscriptionWillRenew);
    }

    [Fact]
    public async Task UnauthorizedWebhook_Returns401AndDoesNotMutateUser()
    {
        var user = await SeedUserAsync();
        var body = BuildEvent(
            type: "INITIAL_PURCHASE",
            appUserId: user.Id.ToString(),
            productId: "premium_monthly",
            entitlementId: "premium",
            store: "APP_STORE",
            periodType: "TRIAL",
            expiresAt: DateTime.UtcNow.AddDays(7));

        var response = await PostWebhookAsync(body, "Bearer wrong_token");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var stored = await ReloadAsync(user.Id);
        Assert.Equal(SubscriptionStatus.None, stored.SubscriptionStatus);
        Assert.Null(stored.SubscriptionEntitlement);
    }

    [Fact]
    public async Task UnknownAppUserId_IsLoggedAndDropped_Returns204()
    {
        // Some other tenant's purchase — we shouldn't error, just ignore.
        var body = BuildEvent(
            type: "INITIAL_PURCHASE",
            appUserId: Guid.NewGuid().ToString(),
            productId: "premium_monthly",
            entitlementId: "premium",
            store: "APP_STORE",
            periodType: "TRIAL",
            expiresAt: DateTime.UtcNow.AddDays(7));

        var response = await PostWebhookAsync(body, BuzzKeeprApiFactory.RevenueCatWebhookToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task<User> SeedUserAsync()
    {
        var email = $"billing-{Guid.NewGuid():N}@buzzkeepr.test";
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            EmailVerified = true,
            CreatedAtUtc = DateTime.UtcNow
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

    private async Task<HttpResponseMessage> PostWebhookAsync(string body, string authorizationHeader)
    {
        var http = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/revenuecat")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Authorization", authorizationHeader);
        return await http.SendAsync(request);
    }

    private static string BuildEvent(
        string type,
        string appUserId,
        string productId,
        string entitlementId,
        string store,
        string periodType,
        DateTime expiresAt,
        DateTime? eventTimestamp = null)
    {
        var payload = new
        {
            api_version = "1.0",
            @event = new
            {
                id = Guid.NewGuid().ToString(),
                type,
                app_user_id = appUserId,
                product_id = productId,
                entitlement_id = entitlementId,
                entitlement_ids = new[] { entitlementId },
                store,
                period_type = periodType,
                event_timestamp_ms = new DateTimeOffset(eventTimestamp ?? DateTime.UtcNow, TimeSpan.Zero).ToUnixTimeMilliseconds(),
                expiration_at_ms = new DateTimeOffset(expiresAt, TimeSpan.Zero).ToUnixTimeMilliseconds()
            }
        };
        return JsonSerializer.Serialize(payload);
    }
}
